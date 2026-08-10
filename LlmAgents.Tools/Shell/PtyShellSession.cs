using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Tools.Shell;

public sealed class PtyShellSession : IShellSession
{
    private readonly ILogger log;
    private readonly string sessionId;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;
    private int? _ptyMasterFd;
    private int? _childPid;
    private int? _exitCode;
    private readonly string? _startError = null;
    private bool _exited;
    private DateTime _startedUtc;

    public int? Pid => _childPid;
    public int? ExitCode => _exitCode;
    public string? StartError => _startError;
    public bool Exited => _exited;
    public DateTime StartedUtc => _startedUtc;

    public event Action<string>? OutputReceived;

    public PtyShellSession(string sessionId, ILogger logger)
    {
        this.sessionId = sessionId;
        this.log = logger;
    }

    public async Task StartAsync(string workingDirectory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            throw new PlatformNotSupportedException("PTY-based shell tools are only supported on Linux and macOS.");
        }

        // Use sh -c "exec bash -i" for better compatibility
        string shellCommand = "export GIT_PAGER= ; export TERM=dumb; exec bash -i || exec sh -i";
        byte[] cmdBytes = Encoding.UTF8.GetBytes(shellCommand + "\0");

        unsafe
        {
            fixed (byte* pCmd = cmdBytes)
            {
                var result = NativeMethods.forkpty_sh(pCmd);

                if (result.child_pid < 0)
                {
                    throw new IOException($"forkpty_sh failed: errno={result.error_code}");
                }

                int status;
                int waitResult = NativeMethods.waitpid(result.child_pid, out status, NativeMethods.WNOHANG);
                if (waitResult > 0)
                {
                    int exitCode = (status >> 8) & 0xFF;
                    int signal = status & 0x7F;
                    throw new IOException($"Child process exited immediately: exitCode={exitCode}, signal={signal}");
                }

                _ptyMasterFd = result.master_fd;
                _childPid = result.child_pid;
                _startedUtc = DateTime.UtcNow;
                _readerCts = new CancellationTokenSource();
                StartPtyReader();

                log.LogInformation("PTY shell started for {SessionId}: pid={Pid}, fd={Fd}", 
                    sessionId, _childPid, _ptyMasterFd);
            }
        }
    }

    private unsafe void StartPtyReader()
    {
        var cts = _readerCts!;
        _readerTask = Task.Run(() =>
        {
            try
            {
                while (_ptyMasterFd.HasValue && !cts.Token.IsCancellationRequested)
                {
                    byte[] buffer = new byte[4096];
                    fixed (byte* ptr = buffer)
                    {
                        int bytesRead = NativeMethods.pty_read(_ptyMasterFd.Value, ptr, buffer.Length);
                        if (bytesRead <= 0)
                        {
                            if (bytesRead < 0)
                            {
                                int errno = Marshal.GetLastPInvokeError();
                                if (errno != 5 || !_exited)
                                {
                                    log.LogWarning("PTY read failed errno={Errno} for {SessionId}", errno, sessionId);
                                }
                            }
                            break;
                        }
                        string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        OutputReceived?.Invoke(text);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.LogError(ex, "PTY reader crashed for {SessionId}", sessionId);
            }
            finally
            {
                // The reader thread owns the master fd. It must be closed here, after
                // the blocking read() has returned, never from another thread while
                // this thread is blocked inside read(). Closing an fd concurrently
                // with a blocked read() on it is undefined behavior and can hang.
                CloseMasterFd();
                _exited = true;
            }
        }, cts.Token);
    }

    private void CloseMasterFd()
    {
        if (_ptyMasterFd.HasValue)
        {
            NativeMethods.pty_close_master(_ptyMasterFd.Value);
            _ptyMasterFd = null;
        }
    }

    public unsafe Task WriteAsync(string input)
    {
        if (!_ptyMasterFd.HasValue) return Task.CompletedTask;

        byte[] bytes = Encoding.UTF8.GetBytes(input);
        fixed (byte* ptr = bytes)
        {
            NativeMethods.pty_write(_ptyMasterFd.Value, ptr, bytes.Length);
        }
        return Task.CompletedTask;
    }

    public Task WriteLineAsync(string input)
    {
        return WriteAsync(input + "\n");
    }

    public Task InterruptAsync()
    {
        WriteAsync("\x03");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        Cleanup();
        await Task.CompletedTask;
    }

    private void Cleanup()
    {
        _exited = true;

        // Kill the child FIRST. This closes the slave side of the PTY, which causes
        // the reader's blocking read() on the master to return EOF and the reader to
        // exit on its own (closing the master fd in its finally block).
        if (_childPid.HasValue)
        {
            NativeMethods.kill(-_childPid.Value, NativeMethods.SIGKILL);
            int status;
            NativeMethods.waitpid(_childPid.Value, out status, 0);
            _exitCode = (status >> 8) & 0xFF;
            _childPid = null;
        }

        if (_readerCts != null)
        {
            _readerCts.Cancel();
            _readerCts.Dispose();
            _readerCts = null;
        }

        // Wait for the reader thread to exit so it closes the master fd itself.
        // This avoids closing the fd while the reader is blocked inside read().
        _readerTask?.Wait(TimeSpan.FromSeconds(2));
        _readerTask = null;
    }

    public void Dispose()
    {
        Cleanup();
    }

    private static unsafe class NativeMethods
    {
        public const string LIBPTYHELPER = "ptyhelper";

        static NativeMethods()
        {
            // Register an explicit resolver so the native library can be found even
            // when the host's default probing fails (e.g. native assets consumed by a
            // global tool, where runtimes/<rid>/native is not probed by default).
            NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLibrary);
        }

        private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, LIBPTYHELPER, StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero; // not ours - fall back to default resolution
            }

            foreach (var candidate in GetCandidatePaths())
            {
                if (File.Exists(candidate))
                {
                    return NativeLibrary.Load(candidate);
                }
            }

            return IntPtr.Zero; // fall back to default resolution (may throw DllNotFoundException)
        }

        private static IEnumerable<string> GetCandidatePaths()
        {
            var (rid, ext) = GetRuntimeInfo();
            var libName = $"libptyhelper.{ext}";
            var assemblyDir = Path.GetDirectoryName(typeof(NativeMethods).Assembly.Location) ?? ".";

            // 1. Standard RID-based runtime layout relative to the assembly
            //    (normal app publish/output, and tool store if deployed).
            yield return Path.Combine(assemblyDir, "runtimes", rid, "native", libName);

            // 2. Flat next to the managed assembly.
            yield return Path.Combine(assemblyDir, libName);

            // 3. NuGet global packages cache for llmagents.tools.
            foreach (var pkgDir in GetNuGetPackageRoots())
            {
                yield return Path.Combine(pkgDir, "runtimes", rid, "native", libName);
                yield return Path.Combine(pkgDir, "lib", "net10.0", libName);
            }

            // 4. Current working directory.
            yield return Path.Combine(Environment.CurrentDirectory, libName);
        }

        private static IEnumerable<string> GetNuGetPackageRoots()
        {
            // ~/.nuget/packages/llmagents.tools/<version>
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                var root = Path.Combine(home, ".nuget", "packages", "llmagents.tools");
                if (Directory.Exists(root))
                {
                    foreach (var versionDir in Directory.EnumerateDirectories(root).OrderByDescending(d => d))
                    {
                        yield return versionDir;
                    }
                }
            }

            // Also probe a common dotnet-tool store path used by the host.
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "/usr/share/dotnet";
            var toolStore = Path.Combine(dotnetRoot, "tools", ".store", "llmagents.tools");
            if (Directory.Exists(toolStore))
            {
                foreach (var versionDir in Directory.EnumerateDirectories(toolStore).OrderByDescending(d => d))
                {
                    yield return versionDir;
                }
            }
        }

        private static (string rid, string ext) GetRuntimeInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var rid = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => "linux-x64",
                    Architecture.Arm64 => "linux-arm64",
                    _ => throw new PlatformNotSupportedException($"Unsupported Linux architecture: {RuntimeInformation.OSArchitecture}")
                };
                return (rid, "so");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var rid = RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => "osx-x64",
                    Architecture.Arm64 => "osx-arm64",
                    _ => throw new PlatformNotSupportedException($"Unsupported macOS architecture: {RuntimeInformation.OSArchitecture}")
                };
                return (rid, "dylib");
            }

            throw new PlatformNotSupportedException($"PTY helper is not supported on {RuntimeInformation.OSDescription}");
        }

        [DllImport(LIBPTYHELPER, EntryPoint = "forkpty_sh", SetLastError = true)]
        public static extern forkpty_result forkpty_sh(byte* shell_command);

        [DllImport(LIBPTYHELPER, EntryPoint = "pty_close_master", SetLastError = true)]
        public static extern int pty_close_master(int master_fd);

        [DllImport(LIBPTYHELPER, EntryPoint = "pty_write", SetLastError = true)]
        public static extern int pty_write(int master_fd, byte* buf, int count);

        [DllImport(LIBPTYHELPER, EntryPoint = "pty_read", SetLastError = true)]
        public static extern int pty_read(int master_fd, byte* buf, int count);

        [DllImport("libc", SetLastError = true)]
        public static extern int kill(int pid, int sig);

        [DllImport("libc", SetLastError = true)]
        public static extern int waitpid(int pid, out int status, int options);

        public const int WNOHANG = 1;
        public const int SIGINT = 2;
        public const int SIGKILL = 9;

        [StructLayout(LayoutKind.Sequential)]
        public struct forkpty_result
        {
            public int master_fd;
            public int child_pid;
            public int error_code;
        }
    }
}
