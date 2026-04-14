using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Tools.Shell;

public sealed class PtyShellSession : IShellSession
{
    private readonly ILogger log;
    private readonly string sessionId;
    private CancellationTokenSource? _readerCts;
    private int? _ptyMasterFd;
    private int? _childPid;
    private int? _exitCode;
    private string? _startError;
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
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("PTY-based shell tools are only supported on Linux.");
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
        _ = Task.Run(() =>
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
                Cleanup();
            }
        }, cts.Token);
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
        if (_readerCts != null)
        {
            _readerCts.Cancel();
            _readerCts.Dispose();
            _readerCts = null;
        }

        if (_ptyMasterFd.HasValue)
        {
            NativeMethods.pty_close_master(_ptyMasterFd.Value);
            _ptyMasterFd = null;
        }

        if (_childPid.HasValue)
        {
            NativeMethods.kill(-_childPid.Value, NativeMethods.SIGKILL);
            int status;
            NativeMethods.waitpid(_childPid.Value, out status, 0);
            _exitCode = (status >> 8) & 0xFF;
            _childPid = null;
        }
    }

    public void Dispose()
    {
        Cleanup();
    }

    private static unsafe class NativeMethods
    {
        public const string PATH_LIBPTYHELPER = "ForkPTY/libptyhelper.so";

        static NativeMethods()
        {
            try
            {
                var assemblyDir = Path.GetDirectoryName(typeof(NativeMethods).Assembly.Location);
                var libPath = Path.Combine(assemblyDir ?? ".", "libptyhelper.so");
                if (!File.Exists(libPath))
                {
                    libPath = PATH_LIBPTYHELPER;
                }
                NativeLibrary.Load(libPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load libptyhelper.so: {ex.Message}");
            }
        }

        [DllImport(PATH_LIBPTYHELPER, EntryPoint = "forkpty_sh", SetLastError = true)]
        public static extern forkpty_result forkpty_sh(byte* shell_command);

        [DllImport(PATH_LIBPTYHELPER, EntryPoint = "pty_close_master", SetLastError = true)]
        public static extern int pty_close_master(int master_fd);

        [DllImport(PATH_LIBPTYHELPER, EntryPoint = "pty_write", SetLastError = true)]
        public static extern int pty_write(int master_fd, byte* buf, int count);

        [DllImport(PATH_LIBPTYHELPER, EntryPoint = "pty_read", SetLastError = true)]
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
