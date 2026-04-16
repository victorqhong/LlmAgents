using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests.Tools;

[TestClass]
public class TestShell
{
    [TestMethod]
    public async Task ExecTimeout_RestartsShell_AndNextCommandSucceeds()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);
        toolFactory.AddParameter("Shell.waitTimeMs", "1000");

        var exec = new ShellExec(toolFactory);
        var read = new ShellRead(toolFactory);
        var session = Session.Ephemeral(loggerFactory);

        var stuckResult = (JsonObject)await exec.Function(session, CreateExecParameters(GetStuckCommand(), waitForExit: true, timeoutMs: 1000));
        Assert.AreEqual("timeout", stuckResult["status"]?.GetValue<string>());

        var recoveryResult = (JsonObject)await exec.Function(session, CreateExecParameters(GetRecoveryCommand(), waitForExit: true));
        Assert.AreEqual("completed", recoveryResult["status"]?.GetValue<string>());

        var output = await ReadAllOutput(read, session);
        Assert.IsTrue(output.Contains("ok", StringComparison.OrdinalIgnoreCase), "expected recovery output to contain 'ok'");
    }

    [TestMethod]
    public async Task Restart_PreservesCurrentDirectory()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);
        toolFactory.AddParameter("Shell.waitTimeMs", "1000");
        toolFactory.AddParameter("restrictToBasePath", "false");
        var bus = new ToolEventBus();
        toolFactory.Register<IToolEventBus>(bus);

        var exec = new ShellExec(toolFactory);
        var read = new ShellRead(toolFactory);
        var directoryChange = new DirectoryChange(toolFactory);
        var session = Session.Ephemeral(loggerFactory);

        var targetDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".."));
        await PostDirectoryChange(bus, directoryChange, session, targetDirectory);

        _ = await exec.Function(session, CreateExecParameters(GetStuckCommand(), waitForExit: true, timeoutMs: 1000));
        _ = await exec.Function(session, CreateExecParameters(GetPwdCommand(), waitForExit: true));

        var output = await ReadAllOutput(read, session);
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        Assert.IsTrue(output.Contains(targetDirectory, comparison));
    }

    [TestMethod]
    public async Task Read_UsesCursorForChunkedOutput()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);

        var exec = new ShellExec(toolFactory);
        var read = new ShellRead(toolFactory);
        var session = Session.Ephemeral(loggerFactory);

        var command = GetEchoLinesCommand("alpha", "beta", "gamma");
        _ = await exec.Function(session, CreateExecParameters(command, waitForExit: true));

        var firstRead = (JsonObject)await read.Function(session, JsonDocument.Parse("""{ "cursor": 0, "max_chars": 8 }"""));
        var firstChunk = firstRead["output"]?.GetValue<string>() ?? string.Empty;
        var nextCursor = firstRead["next_cursor"]?.GetValue<long>() ?? 0;

        var secondRead = (JsonObject)await read.Function(session, JsonDocument.Parse($$"""{ "cursor": {{nextCursor}}, "max_chars": 1024 }"""));
        var secondChunk = secondRead["output"]?.GetValue<string>() ?? string.Empty;

        var output = firstChunk + secondChunk;
        Assert.IsTrue(output.Contains("alpha", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(output.Contains("beta", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(output.Contains("gamma", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Write_SendsInputToInteractiveCommand()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);
        toolFactory.AddParameter("Shell.waitTimeMs", "5000");

        var exec = new ShellExec(toolFactory);
        var write = new ShellWrite(toolFactory);
        var read = new ShellRead(toolFactory);
        var session = Session.Ephemeral(loggerFactory);

        _ = await exec.Function(session, CreateExecParameters(GetInteractiveInputCommand(), waitForExit: false));
        _ = await write.Function(session, JsonDocument.Parse("""{ "input": "Vic", "append_newline": true }"""));
        _ = await exec.Function(session, CreateExecParameters(GetRecoveryCommand(), waitForExit: true));

        var output = await ReadAllOutput(read, session);
        Assert.IsTrue(output.Contains("Hello Vic", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Interrupt_AllowsSubsequentCommands()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);
        toolFactory.AddParameter("Shell.waitTimeMs", "5000");

        var exec = new ShellExec(toolFactory);
        var interrupt = new ShellInterrupt(toolFactory);
        var read = new ShellRead(toolFactory);
        var session = Session.Ephemeral(loggerFactory);

        _ = await exec.Function(session, CreateExecParameters(GetStuckCommand(), waitForExit: false));
        var interruptResult = (JsonObject)await interrupt.Function(session, JsonDocument.Parse("""{ "timeout_ms": 3000 }"""));
        Assert.AreEqual("interrupted", interruptResult["status"]?.GetValue<string>());

        _ = await exec.Function(session, CreateExecParameters(GetRecoveryCommand(), waitForExit: true));
        var output = await ReadAllOutput(read, session);
        Assert.IsTrue(output.Contains("ok", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ReadAllOutput(ShellRead read, Session session)
    {
        long cursor = 0;
        var output = new StringBuilder();
        while (true)
        {
            var result = (JsonObject)await read.Function(session, JsonDocument.Parse($$"""{ "cursor": {{cursor}}, "max_chars": 4096 }"""));
            var chunk = result["output"]?.GetValue<string>() ?? string.Empty;
            output.Append(chunk);
            cursor = result["next_cursor"]?.GetValue<long>() ?? cursor;
            var hasMore = result["has_more"]?.GetValue<bool>() ?? false;
            if (!hasMore)
            {
                break;
            }
        }

        return output.ToString();
    }

    private static JsonDocument CreateExecParameters(string command, bool waitForExit, int? timeoutMs = null)
    {
        var json = new JsonObject
        {
            ["command"] = command,
            ["wait_for_exit"] = waitForExit
        };

        if (timeoutMs.HasValue)
        {
            json["timeout_ms"] = timeoutMs.Value;
        }

        return JsonDocument.Parse(json.ToJsonString());
    }

    private static async Task PostDirectoryChange(ToolEventBus bus, DirectoryChange directoryChange, Session session, string directory)
    {
        var parameters = JsonDocument.Parse($$"""{ "path": "{{directory.Replace("\\", "\\\\")}}" }""");
        var result = await directoryChange.Function(session, parameters);
        bus.PostToolEvent(new ToolCallEvent
        {
            Sender = directoryChange,
            Arguments = parameters,
            Result = result
        });
        await Task.Delay(200);
    }

    private static string GetStuckCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "while ($true) { Start-Sleep -Milliseconds 100 }";
        }

        return "while true; do sleep 1; done";
    }

    private static string GetRecoveryCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Write-Output ok";
        }

        return "echo ok";
    }

    private static string GetInteractiveInputCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "$name = Read-Host 'Name'; Write-Output \"Hello $name\"";
        }

        return "read name; echo \"Hello $name\"";
    }

    private static string GetEchoLinesCommand(params string[] lines)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Join("; ", lines.Select(line => $"Write-Output '{line}'"));
        }

        return string.Join("; ", lines.Select(line => $"echo '{line}'"));
    }

    private static string GetPwdCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "(Get-Location).Path";
        }

        return "pwd";
    }
}
