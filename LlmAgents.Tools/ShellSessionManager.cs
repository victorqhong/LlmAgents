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
        if (state.Session.StartError != null)
        {
            status = "failed_to_start";
        }
        else if (!state.Session.Exited)
        {
            status = "running";
        }
        else
        {
            status = "exited";
        }

        var res = BuildStatus(state, status);
        if (state.Session.StartError != null)
        {
            res["error"] = state.Session.StartError;
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
            await state.Session.StopAsync();
            ClearPendingSentinel(state);
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
            if (state.Session.StartError != null)
            {
                return ErrorJson(state, state.Session.StartError);
            }
            await EnsureProcessStarted(state);
            if (state.Session.StartError != null)
            {
                return ErrorJson(state, state.Session.StartError);
            }
            await state.Session.WriteLineAsync(command);

            if (!waitForExit)
            {
                return new JsonObject
                {
                    ["session_id"] = state.SessionId,
                    ["status"] = "started",
                    ["waited"] = false,
                    ["pid"] = state.Session.Pid
                };
            }

            var timeout = timeoutMs.GetValueOrDefault(waitTimeMs);
            var sentinel = $"__llmagents_eoc_{Guid.NewGuid():N}__";
            var sentinelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetPendingSentinel(state, sentinel, sentinelTask);
            await state.Session.WriteLineAsync(EchoCommand(sentinel));

            var completed = await Task.WhenAny(sentinelTask.Task, Task.Delay(timeout));
            if (completed != sentinelTask.Task)
            {
                log.LogWarning("shell command did not exit after {Timeout} milliseconds", timeout);
                log.LogInformation("{Output}", state.Output.ToString());
                await RestartProcess(state);
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

        if (state.Session.StartError != null)
        {
            return new JsonObject
            {
                ["error"] = state.Session.StartError,
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
            var take = Math.Min(Math.Min(chunkSize, int.MaxValue), available);
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
            if (state.Session.StartError != null)
            {
                return ErrorJson(state, state.Session.StartError);
            }
            await EnsureProcessStarted(state);
            if (state.Session.StartError != null)
            {
                return ErrorJson(state, state.Session.StartError);
            }
            if (appendNewline)
            {
                await state.Session.WriteLineAsync(input);
                return new JsonObject
                {
                    ["session_id"] = state.SessionId,
                    ["status"] = "written",
                    ["written_chars"] = input.Length + Environment.NewLine.Length
                };
            }
            else
            {
                await state.Session.WriteAsync(input);
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
            if (state.Session.StartError != null)
            {
                return ErrorJson(state, state.Session.StartError);
            }
            await EnsureProcessStarted(state);
            if (state.Session.StartError != null)
            {
                return ErrorJson(state, state.Session.StartError);
            }

            await state.Session.InterruptAsync();

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

    private static JsonObject ErrorJson(ShellSessionState state, string error)
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
        return sessions.GetOrAdd(sessionId, _ => 
        {
            var state = new ShellSessionState
            {
                SessionId = sessionId,
                CurrentDirectory = baseCurrentDirectory
            };
            state.Session = CreateSession(state);
            return state;
        });
    }

    private IShellSession CreateSession(ShellSessionState state)
    {
        IShellSession session = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) 
            ? new PtyShellSession(state.SessionId, log) 
            : new ProcessShellSession(state.SessionId, log);
        
        session.OutputReceived += (text) => AppendOutput(state, text);
        return session;
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
                await EnsureProcessStarted(state);
                if (state.Session.StartError == null)
                {
                    await state.Session.WriteLineAsync(ChangeDirectoryCommand(state.CurrentDirectory));
                }
            }
            finally
            {
                state.OperationLock.Release();
            }
        }
    }

    private async Task EnsureProcessStarted(ShellSessionState state)
    {
        if (!state.Session.Exited && state.Session.StartError == null && state.Session.Pid != null) return;
        if (state.Session.StartError != null && state.Session.Exited) return;

        try
        {
            await state.Session.StartAsync(state.CurrentDirectory);
            await state.Session.WriteLineAsync(ChangeDirectoryCommand(state.CurrentDirectory));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to start shell for session {SessionId}", state.SessionId);
        }
    }

    private async Task RestartProcess(ShellSessionState state)
    {
        ClearPendingSentinel(state);
        await state.Session.StopAsync();
        
        // Create a new session instance to replace the old one
        var newSession = CreateSession(state);
        state.Session = newSession;
        
        await EnsureProcessStarted(state);
    }

    private void AppendOutput(ShellSessionState? state, string text)
    {
        if (state == null || string.IsNullOrWhiteSpace(text)) return;

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
                ["pid"] = state.Session.Pid,
                ["currentDirectory"] = state.CurrentDirectory,
                ["started"] = state.Session.StartedUtc,
                ["exit_code"] = state.Session.ExitCode,
                ["buffer_start_cursor"] = state.BufferStartPosition,
                ["buffer_end_cursor"] = end
            };
        }
    }

    private static string EchoCommand(string value) => $"echo '{EscapeBash(value)}'";

    private static string ChangeDirectoryCommand(string directory) => $"cd '{EscapeBash(directory)}'";

    private static string EscapeBash(string value) => value.Replace("'", "'\\''", StringComparison.Ordinal);

    private sealed class ShellSessionState
    {
        public required string SessionId { get; init; }
        public required string CurrentDirectory { get; set; }
        public IShellSession Session { get; set; } = null!;
        public object SyncRoot { get; } = new();
        public SemaphoreSlim OperationLock { get; } = new(1, 1);
        public StringBuilder Output { get; } = new();
        public long BufferStartPosition { get; set; }
        public string? PendingSentinel { get; set; }
        public TaskCompletionSource<bool>? PendingSentinelTcs { get; set; }
    }
}
