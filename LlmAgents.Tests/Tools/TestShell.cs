using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests.Tools;

[TestClass]
public class TestShell
{
    [TestMethod]
    public async Task Timeout_RestartsShell_AndNextCommandSucceeds()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);
        toolFactory.AddParameter("Shell.waitTimeMs", "1000");
        var shell = new Shell(toolFactory);

        var stuck = JsonDocument.Parse($$"""{ "command": "{{GetStuckCommand()}}" }""");
        var stuckResult = (JsonObject)await shell.Function(Session.New(), stuck);

        Assert.IsTrue(stuckResult.ContainsKey("warning"));

        // next command should succeed due to automatic shell restart
        var recovery = JsonDocument.Parse($$"""{ "command": "{{GetRecoveryCommand()}}" }""");
        var recoveryResult = (JsonObject)await shell.Function(Session.New(), recovery);
        var stdout = recoveryResult["stdout"]?.GetValue<string>() ?? string.Empty;

        Assert.IsFalse(recoveryResult.ContainsKey("warning"));
        Assert.IsTrue(stdout.Contains("ok", StringComparison.OrdinalIgnoreCase));
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

        var shell = new Shell(toolFactory);
        var directoryChange = new DirectoryChange(toolFactory);

        var targetDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".."));
        await PostDirectoryChange(bus, directoryChange, Session.New(), targetDirectory);

        // trigger timeout and restart
        var stuck = JsonDocument.Parse($$"""{ "command": "{{GetStuckCommand()}}" }""");
        _ = await shell.Function(Session.New(), stuck);

        // verify cwd preserved after restart
        var pwd = JsonDocument.Parse($$"""{ "command": "{{GetPwdCommand()}}" }""");
        var pwdResult = (JsonObject)await shell.Function(Session.New(), pwd);
        var stdout = pwdResult["stdout"]?.GetValue<string>() ?? string.Empty;

        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        Assert.IsTrue(stdout.Contains(targetDirectory, comparison));
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

    private static string GetPwdCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "(Get-Location).Path";
        }

        return "pwd";
    }
}
