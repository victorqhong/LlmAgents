using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using LlmAgents.State;
using Microsoft.Extensions.Logging;
using LlmAgents.Tools.Events;

namespace LlmAgents.Tools;

public sealed class ShellSessionManager
{
    private const int defaultReadMaxChars = 4096;
    private static readonly string defaultSessionId = "__default__";

    private readonly ILogger log;
    private readonly int waitTimeMs;
    private readonly int maxBufferedChars;
    private readonly ConcurrentDictionary<string, ShellSessionState> sessions = new();

    private string baseCurrentDirectory;

    public ShellSessionManager(ToolFactory toolFactory, ILogger logger)
    {
        log = logger;
        waitTimeMs = int.TryParse(toolFactory.GetParameter("Shell.waitTimeMs"), out var parsedWait) ? parsedWait : 10_000;
        maxBufferedChars = int.TryParse(toolFactory.GetParameter("Shell.maxBufferedChars"), out var parsedMax) ? parsedMax : 250000;
        baseCurrentDirectory = Path.GetFullPath(toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory);

        var toolEventBus = toolFactory.ResolveWithDefault<IToolEventBus>();
        toolEventBus?.SubscribeToolEvent<DirectoryChange>(OnChangeDirectory);
    }

    public JsonNode Status(Session? session)
    {
        var sessionId = GetSessionId(session);
        if (!sessions.TryGetValue(sessionId, out var state))
        {
            return new JsonObject
            {
                ["session_id"] = sessionId,
                ["status"] = "not_started"
            };
        }

        string status;
        if (state.StartError != null)
        {
            status = "failed_to_start";
        }
        else if (state.PtyMasterFd.HasValue)
        {
            status = "running";
        }
        else
        {
            status = "exited";
        }

        var res = BuildStatus(state, status);
        if (state.StartError != null)
        {
            ((JsonObject)res)["error"] = state.StartError;
        }
        return res;
    }

    public async Task<JsonNode> StopAsync(Session? session)
    {
        var sessionId = GetSessionId(session);
        if (!sessions.TryRemove(sessionId, out var state))
        {
            return new JsonObject
            {
                ["session_id"] = sessionId,
                ["status"] = "not_started"
            };
        }

        await state.OperationLock.WaitAsync();
        try
        {
            DisposePty(state);
            ClearPendingSentinel(state);
            state.StartError = null;
            return new JsonObject
            {
                ["session_id"] = sessionId,
                ["status"] = "stopped"
            };
        }
        finally
        {
            state.OperationLock.Release();
        }
    }

    public async Task<JsonNode> ExecAsync(Session? session, string command, bool waitForExit, int? timeoutMs)
    {
        var state = GetOrCreateState(GetSessionId(session));
        await state.OperationLock.WaitAsync();
        try
        {
            if (state.StartError != null)
            {
                return ErrorJson(state, state.StartError);
            }
            EnsureProcessStarted(state);
            if (state.StartError != null)
            {
                return ErrorJson(state, state.StartError);
            }
            WriteLinePty(state, command);

            // if (!waitForExit)
            if (true)
            {
                return new JsonObject
                {
                    ["session_id"] = state.SessionId,
                    ["status"] = "started",
                    ["waited"] = false,
                    ["pid"] = state.ChildPid
                };
            }

            var timeout = timeoutMs.GetValueOrDefault(waitTimeMs);
            var sentinel = $"__llmagents_eoc_{Guid.NewGuid():N}__";
            var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetPendingSentinel(state, sentinel, sentinelTask);
            WriteLinePty(state, EchoCommand(sentinel));

            var completed = await Task.WhenAny(sentinelTask.Task, Task.Delay(timeout));
            if (completed != sentinelTask.Task)
            {
                log.LogWarning("shell command did not exit after {Timeout} milliseconds", timeout);
                log.LogInformation("{Output}", state.Output.ToString());
                RestartProcess(state);
                return new JsonObject
                {
                    ["session_id"] = state.SessionId,
                    ["status"] = "timeout",
                    ["waited"] = true,
                    ["warning"] = $"shell command did not exit after {timeout} milliseconds; shell process restarted"
                };
            }

            return new JsonObject
            {
                ["session_id"] = state.SessionId,
                ["status"] = "completed",
                ["waited"] = true
            };
        }
        finally
        {
            ClearPendingSentinel(state);
            state.OperationLock.Release();
        }
    }

    public JsonNode Read(Session? session, int? cursor, int? maxChars)
    {
        var sessionId = GetSessionId(session);
        if (!sessions.TryGetValue(sessionId, out var state))
        {
            return new JsonObject
            {
                ["error"] = "shell session not started",
                ["session_id"] = sessionId
            };
        }

        if (state.StartError != null)
        {
            return new JsonObject
            {
                ["error"] = state.StartError,
                ["session_id"] = sessionId
            };
        }

        lock (state.SyncRoot)
        {
            var start = state.BufferStartPosition;
            var end = start + state.Output.Length;
            long resolvedCursor = cursor ?? start;

            var truncated = false;
            if (resolvedCursor < start)
            {
                truncated = true;
                resolvedCursor = start;
            }
            else if (resolvedCursor > end)
            {
                resolvedCursor = end;
            }

            var chunkSize = Math.Max(maxChars ?? defaultReadMaxChars, 0);
            var offset = (int)(resolvedCursor - start);
            var available = Math.Max(0, state.Output.Length - offset);
            var take = Math.Min((int)Math.Min(chunkSize, int.MaxValue), available);
            var output = take > 0 ? state.Output.ToString(offset, take) : string.Empty;
            var nextCursor = resolvedCursor + take;

            return new JsonObject
            {
                ["session_id"] = state.SessionId,
                ["output"] = output,
                ["next_cursor"] = nextCursor,
                ["has_more"] = nextCursor < end,
                ["buffer_start_cursor"] = start,
                ["buffer_end_cursor"] = end,
                ["truncated"] = truncated
            };
        }
    }

    public async Task<JsonNode> WriteAsync(Session? session, string input, bool appendNewline)
    {
        var state = GetOrCreateState(GetSessionId(session));
        await state.OperationLock.WaitAsync();
        try
        {
            if (state.StartError != null)
            {
                return ErrorJson(state, state.StartError);
            }
            EnsureProcessStarted(state);
            if (state.StartError != null)
            {
                return ErrorJson(state, state.StartError);
            }
            if (appendNewline)
            {
                WriteLinePty(state, input);
                return new JsonObject
                {
                    ["session_id"] = state.SessionId,
                    ["status"] = "written",
                    ["written_chars"] = input.Length + Environment.NewLine.Length
                };
            }
            else
            {
                WritePty(state, input);
                return new JsonObject
                {
                    ["session_id"] = state.SessionId,
                    ["status"] = "written",
                    ["written_chars"] = input.Length
                };
            }
        }
        finally
        {
            state.OperationLock.Release();
        }
    }

    public async Task<JsonNode> InterruptAsync(Session? session, int? timeoutMs)
    {
        var state = GetOrCreateState(GetSessionId(session));
        await state.OperationLock.WaitAsync();
        try
        {
            if (state.StartError != null)
            {
                return ErrorJson(state, state.StartError);
            }
            EnsureProcessStarted(state);
            if (state.StartError != null)
            {
                return ErrorJson(state, state.StartError);
            }

            WritePty(state, "\x03");

            // var timeout = timeoutMs.GetValueOrDefault(Math.Min(waitTimeMs, 3000));
            // var sentinel = $"__llmagents_interrupt_probe_{Guid.NewGuid():N}__";
            // var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // SetPendingSentinel(state, sentinel, sentinelTask);
            // WriteLinePty(state, EchoCommand(sentinel));
            //
            // var completed = await Task.WhenAny(sentinelTask.Task, Task.Delay(timeout));
            // var responsive = completed == sentinelTask.Task;
            // if (!responsive)
            // {
            //     RestartProcess(state);
            // }

            return new JsonObject
            {
                ["session_id"] = state.SessionId,
                ["status"] = "interrupted",
            };
        }
        finally
        {
            ClearPendingSentinel(state);
            state.OperationLock.Release();
        }
    }

    private static JsonNode ErrorJson(ShellSessionState state, string error)
    {
        return new JsonObject
        {
            ["session_id"] = state.SessionId,
            ["status"] = "error",
            ["error"] = error
        };
    }

    private static string GetSessionId(Session? session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
        {
            return defaultSessionId;
        }
        return session.SessionId;
    }

    private ShellSessionState GetOrCreateState(string sessionId)
    {
        return sessions.GetOrAdd(sessionId, _ => new ShellSessionState
        {
            SessionId = sessionId,
            CurrentDirectory = baseCurrentDirectory
        });
    }

    private async Task OnChangeDirectory(ToolEvent e)
    {
        string? newDir = null;
        if (e is ChangeDirectoryEvent cde)
        {
            newDir = cde.Directory;
        }
        else if (e is ToolCallEvent tce &&
            tce.Result.AsObject() is JsonObject jsonObject &&
            jsonObject.TryGetPropertyValue("currentDirectory", out var property))
        {
            newDir = property?.GetValue<string>();
        }
        if (newDir == null)
        {
            return;
        }

        baseCurrentDirectory = Path.GetFullPath(newDir ?? baseCurrentDirectory);

        var copy = sessions.Values.ToArray();
        foreach (var state in copy)
        {
            await state.OperationLock.WaitAsync();
            try
            {
                state.CurrentDirectory = baseCurrentDirectory;
                EnsureProcessStarted(state);
                if (state.StartError == null)
                {
                    WriteLinePty(state, ChangeDirectoryCommand(state.CurrentDirectory));
                }
            }
            finally
            {
                state.OperationLock.Release();
            }
        }
    }

    private void EnsureProcessStarted(ShellSessionState state)
    {
        if (state.PtyMasterFd.HasValue && state.ReaderCts != null) return;
        if (state.StartError != null) return;

        try
        {
            StartProcess(state);
            WriteLinePty(state, ChangeDirectoryCommand(state.CurrentDirectory));
        }
        catch (Exception ex)
        {
            state.StartError = ex.Message;
            log.LogError(ex, "Failed to start PTY shell for session {SessionId}", state.SessionId);
        }
    }

    private unsafe void StartProcess(ShellSessionState state)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            throw new PlatformNotSupportedException("PTY-based shell tools are only supported on Linux.");
        }

        // Use sh -c "exec bash -i" for better NixOS compatibility
        // This lets sh find bash in PATH while giving us a proper interactive shell
        string shellCommand = "export GIT_PAGER= ; export TERM=dumb; exec bash -i || exec sh -i";
        
        byte[] cmdBytes = Encoding.UTF8.GetBytes(shellCommand + "\0");
        
        fixed (byte* pCmd = cmdBytes)
        {
            var result = NativeMethods.forkpty_sh(pCmd);
            
            if (result.child_pid < 0)
            {
                throw new IOException($"forkpty_sh failed: errno={result.error_code}");
            }
            
            // Quick non-blocking check if child exited immediately (exec failure)
            int status;
            int waitResult = NativeMethods.waitpid(result.child_pid, out status, NativeMethods.WNOHANG);
            if (waitResult > 0)
            {
                int exitCode = (status >> 8) & 0xFF;
                int signal = status & 0x7F;
                throw new IOException($"Child process exited immediately: exitCode={exitCode}, signal={signal}");
            }
            
            state.PtyMasterFd = result.master_fd;
            state.ChildPid = result.child_pid;
            state.StartedUtc = DateTime.UtcNow;
            state.ReaderCts = new CancellationTokenSource();
            StartPtyReader(state);
            
            log.LogInformation("PTY shell started for {SessionId}: pid={ChildPid}, fd={PtyMasterFd}", 
                state.SessionId, result.child_pid, result.master_fd);
        }
    }

    private unsafe void StartPtyReader(ShellSessionState state)
    {
        var cts = state.ReaderCts!;
        _ = Task.Run(() =>
        {
            try
            {
                while (state.PtyMasterFd.HasValue && !cts.Token.IsCancellationRequested)
                {
                    byte[] buffer = new byte[4096];
                    fixed (byte* ptr = buffer)
                    {
                        int bytesRead = NativeMethods.pty_read(state.PtyMasterFd.Value, ptr, buffer.Length);
                        if (bytesRead <= 0)
                        {
                            if (bytesRead < 0)
                            {
                                int errno = Marshal.GetLastPInvokeError();
                                // EIO (5) during dispose is expected - only warn if not during cleanup
                                if (errno != 5 || !state.Exited.GetValueOrDefault())
                                {
                                    log.LogWarning("PTY read failed errno={Errno} for {SessionId}", errno, state.SessionId);
                                }
                            }
                            break;
                        }
                        string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        AppendOutput(state, text);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                log.LogError(ex, "PTY reader crashed for {SessionId}", state.SessionId);
            }
            finally
            {
                if (state.ChildPid.HasValue)
                {
                    int status;
                    NativeMethods.waitpid(state.ChildPid.Value, out status, 0);
                    state.ExitCode = (status >> 8) & 0xFF;
                    state.ChildPid = null;
                }
                if (state.PtyMasterFd.HasValue)
                {
                    NativeMethods.pty_close_master(state.PtyMasterFd.Value);
                    state.PtyMasterFd = null;
                }
                state.Exited = true;
            }
        }, cts.Token);
    }

    private void RestartProcess(ShellSessionState state)
    {
        ClearPendingSentinel(state);
        DisposePty(state);
        state.StartError = null;
        state.Exited = false;
        state.ExitCode = null;
        StartProcess(state);
        WriteLinePty(state, ChangeDirectoryCommand(state.CurrentDirectory));
    }

    private void AppendOutput(ShellSessionState state, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (state.SyncRoot)
        {
            if (!string.IsNullOrEmpty(state.PendingSentinel))
            {
                if (text.Trim().Equals(state.PendingSentinel))
                {
                    state.PendingSentinelTcs?.TrySetResult(true);
                    state.PendingSentinel = null;
                    state.PendingSentinelTcs = null;
                }
                else if (!text.Contains(state.PendingSentinel))
                {
                    state.Output.Append(text);
                }
            }
            else
            {
                state.Output.Append(text);
            }

            while (state.Output.Length > maxBufferedChars)
            {
                var remove = state.Output.Length - maxBufferedChars;
                state.Output.Remove(0, remove);
                state.BufferStartPosition += remove;
            }
        }

        log.LogDebug("Shell {SessionId}: {Preview}", state.SessionId, text.TrimEnd().Take(100));
    }

    private unsafe void WritePty(ShellSessionState state, string input)
    {
        if (!state.PtyMasterFd.HasValue) return;

        byte[] bytes = Encoding.UTF8.GetBytes(input);
        fixed (byte* ptr = bytes)
        {
            NativeMethods.pty_write(state.PtyMasterFd.Value, ptr, bytes.Length);
        }
    }

    private void WriteLinePty(ShellSessionState state, string line)
    {
        WritePty(state, line + "\n");
    }

    private static void SetPendingSentinel(ShellSessionState state, string sentinel, TaskCompletionSource<bool> tcs)
    {
        lock (state.SyncRoot)
        {
            state.PendingSentinel = sentinel;
            state.PendingSentinelTcs = tcs;
        }
    }

    private static void ClearPendingSentinel(ShellSessionState state)
    {
        lock (state.SyncRoot)
        {
            state.PendingSentinelTcs?.TrySetCanceled();
            state.PendingSentinel = null;
            state.PendingSentinelTcs = null;
        }
    }

    private JsonObject BuildStatus(ShellSessionState state, string status)
    {
        lock (state.SyncRoot)
        {
            var end = state.BufferStartPosition + state.Output.Length;
            return new JsonObject
            {
                ["session_id"] = state.SessionId,
                ["status"] = status,
                ["pid"] = state.ChildPid,
                ["currentDirectory"] = state.CurrentDirectory,
                ["started"] = state.StartedUtc,
                ["exit_code"] = state.ExitCode,
                ["buffer_start_cursor"] = state.BufferStartPosition,
                ["buffer_end_cursor"] = end
            };
        }
    }

    private static string EchoCommand(string value) => $"echo '{EscapeBash(value)}'";

    private static string ChangeDirectoryCommand(string directory) => $"cd '{EscapeBash(directory)}'";

    private static string EscapeBash(string value) => value.Replace("'", "'\\''", StringComparison.Ordinal);

    private void DisposePty(ShellSessionState state)
    {
        state.Exited = true;  // Signal reader to suppress EIO warnings
        
        if (state.ReaderCts != null)
        {
            state.ReaderCts.Cancel();
            state.ReaderCts.Dispose();
            state.ReaderCts = null;
        }

        if (state.PtyMasterFd.HasValue)
        {
            NativeMethods.pty_close_master(state.PtyMasterFd.Value);
            state.PtyMasterFd = null;
        }

        if (state.ChildPid.HasValue)
        {
            NativeMethods.kill(-state.ChildPid.Value, NativeMethods.SIGKILL);
            int status;
            NativeMethods.waitpid(state.ChildPid.Value, out status, 0);
            state.ExitCode = (status >> 8) & 0xFF;
            state.ChildPid = null;
        }
    }

    private static unsafe class NativeMethods
    {
        public const string PATH_LIBPTYHELPER = "/home/victor/Code/LlmAgents/forkpty/libptyhelper.so";

        // Load the helper library on first use
        static NativeMethods()
        {
            try
            {
                // Try to load from same directory as assembly first
                var assemblyDir = Path.GetDirectoryName(typeof(NativeMethods).Assembly.Location);
                var libPath = Path.Combine(assemblyDir ?? ".", "libptyhelper.so");
                
                if (!File.Exists(libPath))
                {
                    // Fallback to relative path
                    libPath = PATH_LIBPTYHELPER;
                }
                
                NativeLibrary.Load(libPath);
            }
            catch (Exception ex)
            {
                // If library loading fails, will get P/Invoke errors later
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

        // Standard libc functions
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

    private sealed class ShellSessionState
    {
        public required string SessionId { get; init; }
        public required string CurrentDirectory { get; set; }
        public DateTime StartedUtc { get; set; }
        public int? ExitCode { get; set; }
        public bool? Exited { get; set; }
        public string? StartError { get; set; }
        public int? PtyMasterFd { get; set; }
        public int? ChildPid { get; set; }
        public CancellationTokenSource? ReaderCts { get; set; }
        public object SyncRoot { get; } = new();
        public SemaphoreSlim OperationLock { get; } = new(1, 1);
        public StringBuilder Output { get; } = new();
        public long BufferStartPosition { get; set; }
        public string? PendingSentinel { get; set; }
        public TaskCompletionSource<bool>? PendingSentinelTcs { get; set; }
    }
}
