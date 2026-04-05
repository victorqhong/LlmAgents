namespace LlmAgents.Tools;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using LlmAgents.State;
using Microsoft.Extensions.Logging;

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
        waitTimeMs = int.TryParse(toolFactory.GetParameter("Shell.waitTimeMs"), out waitTimeMs) ? waitTimeMs : 180000;
        maxBufferedChars = int.TryParse(toolFactory.GetParameter("Shell.maxBufferedChars"), out maxBufferedChars) ? maxBufferedChars : 250000;
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
                { "session_id", sessionId },
                { "status", "not_started" }
            };
        }

        return BuildStatus(state, state.Process?.HasExited == false ? "running" : "exited");
    }

    public async Task<JsonNode> StopAsync(Session? session)
    {
        var sessionId = GetSessionId(session);
        if (!sessions.TryRemove(sessionId, out var state))
        {
            return new JsonObject
            {
                { "session_id", sessionId },
                { "status", "not_started" }
            };
        }

        await state.OperationLock.WaitAsync();
        try
        {
            DisposeProcess(state.Process);
            state.PendingSentinelTcs?.TrySetCanceled();
            state.PendingSentinel = null;
            state.PendingSentinelTcs = null;
            return new JsonObject
            {
                { "session_id", sessionId },
                { "status", "stopped" }
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
            EnsureProcessStarted(state);
            WriteLine(state, command);

            if (!waitForExit)
            {
                return new JsonObject
                {
                    { "session_id", state.SessionId },
                    { "status", "started" },
                    { "waited", false },
                    { "pid", state.Process.Id }
                };
            }

            var timeout = timeoutMs.GetValueOrDefault(waitTimeMs);
            var sentinel = $"__llmagents_eoc_{Guid.NewGuid():N}__";
            var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetPendingSentinel(state, sentinel, sentinelTask);
            WriteLine(state, EchoCommand(sentinel));

            var completed = await Task.WhenAny(sentinelTask.Task, Task.Delay(timeout));
            if (completed != sentinelTask.Task)
            {
                log.LogWarning("shell command did not exit after {waitTimeMs} milliseconds", timeout);
                RestartProcess(state);
                return new JsonObject
                {
                    { "session_id", state.SessionId },
                    { "status", "timeout" },
                    { "waited", true },
                    { "warning", $"shell command did not exit after {timeout} milliseconds; shell process restarted" }
                };
            }

            return new JsonObject
            {
                { "session_id", state.SessionId },
                { "status", "completed" },
                { "waited", true }
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
                { "error", "shell session not started" },
                { "session_id", sessionId }
            };
        }

        lock (state.SyncRoot)
        {
            var start = state.BufferStartPosition;
            var end = start + state.Output.Length;
            long resolvedCursor = cursor.HasValue ? cursor.Value : start;

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

            var chunkSize = maxChars.GetValueOrDefault(defaultReadMaxChars);
            if (chunkSize <= 0)
            {
                chunkSize = defaultReadMaxChars;
            }

            var offset = (int)(resolvedCursor - start);
            var available = Math.Max(0, state.Output.Length - offset);
            var take = Math.Min(chunkSize, available);
            var output = take > 0 ? state.Output.ToString(offset, take) : string.Empty;
            var nextCursor = resolvedCursor + take;

            return new JsonObject
            {
                { "session_id", state.SessionId },
                { "output", output },
                { "next_cursor", nextCursor },
                { "has_more", nextCursor < end },
                { "buffer_start_cursor", start },
                { "buffer_end_cursor", end },
                { "truncated", truncated }
            };
        }
    }

    public async Task<JsonNode> WriteAsync(Session? session, string input, bool appendNewline)
    {
        var state = GetOrCreateState(GetSessionId(session));
        await state.OperationLock.WaitAsync();
        try
        {
            EnsureProcessStarted(state);
            if (appendNewline)
            {
                state.Process.StandardInput.WriteLine(input);
            }
            else
            {
                state.Process.StandardInput.Write(input);
            }

            state.Process.StandardInput.Flush();
            return new JsonObject
            {
                { "session_id", state.SessionId },
                { "status", "written" },
                { "written_chars", input.Length + (appendNewline ? Environment.NewLine.Length : 0) }
            };
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
            EnsureProcessStarted(state);

            state.Process.StandardInput.Write("\u0003");
            state.Process.StandardInput.Flush();

            var timeout = timeoutMs.GetValueOrDefault(Math.Min(waitTimeMs, 3000));
            var sentinel = $"__llmagents_interrupt_probe_{Guid.NewGuid():N}__";
            var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetPendingSentinel(state, sentinel, sentinelTask);
            WriteLine(state, EchoCommand(sentinel));

            var completed = await Task.WhenAny(sentinelTask.Task, Task.Delay(timeout));
            var responsive = completed == sentinelTask.Task;
            if (!responsive)
            {
                RestartProcess(state);
            }

            return new JsonObject
            {
                { "session_id", state.SessionId },
                { "status", "interrupted" },
                { "responsive", responsive },
                { "restarted", !responsive }
            };
        }
        finally
        {
            ClearPendingSentinel(state);
            state.OperationLock.Release();
        }
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
        if (e is ToolCallEvent tce &&
            tce.Result.AsObject() is JsonObject jsonObject &&
            jsonObject.TryGetPropertyValue("currentDirectory", out var property))
        {
            baseCurrentDirectory = Path.GetFullPath(property?.GetValue<string>() ?? baseCurrentDirectory);
        }
        else if (e is Events.ChangeDirectoryEvent cde)
        {
            baseCurrentDirectory = Path.GetFullPath(cde.Directory);
        }
        else
        {
            return;
        }

        var copy = sessions.Values.ToArray();
        foreach (var state in copy)
        {
            await state.OperationLock.WaitAsync();
            try
            {
                state.CurrentDirectory = baseCurrentDirectory;
                EnsureProcessStarted(state);
                WriteLine(state, ChangeDirectoryCommand(state.CurrentDirectory));
            }
            finally
            {
                state.OperationLock.Release();
            }
        }
    }

    private void EnsureProcessStarted(ShellSessionState state)
    {
        if (state.Process != null && !state.Process.HasExited)
        {
            return;
        }

        StartProcess(state);
        WriteLine(state, ChangeDirectoryCommand(state.CurrentDirectory));
    }

    private void StartProcess(ShellSessionState state)
    {
        var process = new Process();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.ArgumentList.Add("-NoLogo");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            process.StartInfo.FileName = "bash";
        }
        else
        {
            throw new NotImplementedException(RuntimeInformation.RuntimeIdentifier);
        }

        process.StartInfo.WorkingDirectory = Path.GetFullPath(state.CurrentDirectory);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.RedirectStandardInput = true;
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (_, e) => OnOutputLine(state, e.Data, isError: false);
        process.ErrorDataReceived += (_, e) => OnOutputLine(state, e.Data, isError: true);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        state.Process = process;
        state.StartedUtc = DateTime.UtcNow;
        state.ExitCode = null;
        process.Exited += (_, _) => state.ExitCode = process.ExitCode;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WriteLine(state, "$PSStyle.OutputRendering='Plain'");
        }
    }

    private void RestartProcess(ShellSessionState state)
    {
        DisposeProcess(state.Process);
        state.PendingSentinelTcs?.TrySetCanceled();
        state.PendingSentinelTcs = null;
        state.PendingSentinel = null;
        StartProcess(state);
        WriteLine(state, ChangeDirectoryCommand(state.CurrentDirectory));
    }

    private void OnOutputLine(ShellSessionState state, string? line, bool isError)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        var isSentinel = false;
        lock (state.SyncRoot)
        {
            if (!string.IsNullOrEmpty(state.PendingSentinel) &&
                string.Equals(line.Trim(), state.PendingSentinel, StringComparison.Ordinal))
            {
                state.PendingSentinelTcs?.TrySetResult(true);
                state.PendingSentinel = null;
                state.PendingSentinelTcs = null;
                isSentinel = true;
            }
        }

        if (isSentinel)
        {
            return;
        }

        AppendOutput(state, line + Environment.NewLine);
        log.LogInformation("{stream}: {line}", isError ? "stderr" : "stdout", line);
    }

    private void AppendOutput(ShellSessionState state, string text)
    {
        lock (state.SyncRoot)
        {
            state.Output.Append(text);
            if (state.Output.Length <= maxBufferedChars)
            {
                return;
            }

            var remove = state.Output.Length - maxBufferedChars;
            state.Output.Remove(0, remove);
            state.BufferStartPosition += remove;
        }
    }

    private static void WriteLine(ShellSessionState state, string line)
    {
        state.Process.StandardInput.WriteLine(line);
        state.Process.StandardInput.Flush();
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
            state.PendingSentinel = null;
            state.PendingSentinelTcs = null;
        }
    }

    private static JsonObject BuildStatus(ShellSessionState state, string status)
    {
        lock (state.SyncRoot)
        {
            var end = state.BufferStartPosition + state.Output.Length;
            return new JsonObject
            {
                { "session_id", state.SessionId },
                { "status", status },
                { "pid", state.Process?.HasExited == false ? state.Process.Id : null },
                { "currentDirectory", state.CurrentDirectory },
                { "started", state.StartedUtc },
                { "exit_code", state.ExitCode },
                { "buffer_start_cursor", state.BufferStartPosition },
                { "buffer_end_cursor", end }
            };
        }
    }

    private static string EchoCommand(string value)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"Write-Output '{EscapePwsh(value)}'";
        }

        return $"echo '{EscapeBash(value)}'";
    }

    private static string ChangeDirectoryCommand(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"Set-Location -LiteralPath '{EscapePwsh(directory)}'";
        }

        return $"cd '{EscapeBash(directory)}'";
    }

    private static string EscapePwsh(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string EscapeBash(string value) => value.Replace("'", "'\"'\"'", StringComparison.Ordinal);

    private static void DisposeProcess(Process? process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed class ShellSessionState
    {
        public required string SessionId { get; init; }
        public required string CurrentDirectory { get; set; }
        public Process Process { get; set; } = null!;
        public DateTime StartedUtc { get; set; }
        public int? ExitCode { get; set; }
        public object SyncRoot { get; } = new();
        public SemaphoreSlim OperationLock { get; } = new(1, 1);
        public StringBuilder Output { get; } = new();
        public long BufferStartPosition { get; set; }
        public string? PendingSentinel { get; set; }
        public TaskCompletionSource<bool>? PendingSentinelTcs { get; set; }
    }
}
