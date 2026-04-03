namespace LlmAgents.Tools;

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using Microsoft.Extensions.Logging;

public class Shell : Tool
{
    private const int waitCheckTimeMs = 1000;

    private static string shellName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PowerShell Core" : "bash";

    private static string echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Write-Host" : "echo";

    private readonly ILogger Log;

    private readonly int waitTimeMs;

    private readonly StringBuilder stdout = new StringBuilder();
    private readonly StringBuilder stderr = new StringBuilder();

    private string? commandSentinel;
    private bool receivedOutput = false;

    private string currentDirectory;

    public Shell(ToolFactory toolFactory)
        : base(toolFactory)
    {
        Log = toolFactory.Resolve<ILoggerFactory>().CreateLogger(nameof(Shell));

        waitTimeMs = int.TryParse(toolFactory.GetParameter($"{nameof(Shell)}.{nameof(waitTimeMs)}"), out waitTimeMs) ? waitTimeMs : 180000;

        currentDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;

        Process = StartShellProcess();

        var toolEventBus = toolFactory.ResolveWithDefault<IToolEventBus>();
        toolEventBus?.SubscribeToolEvent<DirectoryChange>(OnChangeDirectory);
    }

    public Process Process { get; private set; }

    public CancellationTokenSource CancellationTokenSource { get; private set; } = new();

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "shell",
            Description = $"Runs a shell command in {shellName}. Prefer the 'file_write' tool for writing files.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "command", new() { Type = "string", Description = "Shell command and arguments to run" } }
                },
                Required = [ "command" ]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("command", string.Empty, out var command) || string.IsNullOrEmpty(command))
        {
            result.Add("error", "command is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        try
        {
            commandSentinel = $"__llmagents_eoc_{Guid.NewGuid()}__";
            var commandDelimiter = $"{echoCommand} \"{commandSentinel}\"";

            CancellationTokenSource = new CancellationTokenSource();

            receivedOutput = false;

            stdout.Clear();
            stderr.Clear();
            EnsureShellProcess();
            Process.StandardInput.WriteLine(command);
            Process.StandardInput.WriteLine(commandDelimiter);
            Process.StandardInput.Flush();

            var totalWaitTimeMs = 0;
            while (!receivedOutput)
            {
                if (CancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }

                if (totalWaitTimeMs >= waitTimeMs)
                {
                    Log.LogInformation("shell command did not exit after {waitTimeMs} milliseconds", waitTimeMs);
                    break;
                }

                Thread.Sleep(waitCheckTimeMs);
                totalWaitTimeMs += waitCheckTimeMs;
            }

            if (!receivedOutput)
            {
                result.Add("warning", $"shell did not exit and may still be running");
                RestartShellProcess();
            }

            result.Add("stdout", stdout.ToString());
            result.Add("stderr", stderr.ToString());
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
            RestartShellProcess();
        }

        return Task.FromResult<JsonNode>(result);
    }

    private Task OnChangeDirectory(ToolEvent e)
    {
        if (e is ToolCallEvent tce && tce.Result.AsObject() is JsonObject jsonObject && jsonObject.TryGetPropertyValue("currentDirectory", out var property))
        {
            currentDirectory = property?.GetValue<string>() ?? currentDirectory;
        }
        else if (e is Events.ChangeDirectoryEvent cde)
        {
            currentDirectory = cde.Directory;
        }

        EnsureShellProcess();
        Process.StandardInput.WriteLine($"cd \"{currentDirectory}\"");

        return Task.CompletedTask;
    }

    private Process StartShellProcess()
    {
        var process = new Process();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.ArgumentList.Add("-NoLogo");
            process.StartInfo.ArgumentList.Add("-NonInteractive");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            process.StartInfo.FileName = "bash";
        }
        else
        {
            throw new NotImplementedException(RuntimeInformation.RuntimeIdentifier);
        }

        process.StartInfo.WorkingDirectory = Path.IsPathFullyQualified(currentDirectory) ? currentDirectory : Path.Combine(Environment.CurrentDirectory, currentDirectory);
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardError = true;
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            if (!string.IsNullOrEmpty(commandSentinel) && string.Equals(e.Data.Trim(), commandSentinel, StringComparison.Ordinal))
            {
                receivedOutput = true;
                return;
            }

            stdout.AppendLine(e.Data);
            Log.LogInformation("{data}", e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            stderr.AppendLine(e.Data);
            Log.LogInformation("{data}", e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.StandardInput.WriteLine("$PSStyle.OutputRendering='Plain'");
        process.StandardInput.Flush();

        return process;
    }

    private void RestartShellProcess()
    {
        DisposeShellProcess();
        Process = StartShellProcess();
        receivedOutput = true;
        commandSentinel = null;
    }

    private void DisposeShellProcess()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(2000);
            }
        }
        catch (Exception e)
        {
            Log.LogWarning(e, "Exception while disposing shell process");
        }
        finally
        {
            Process.Dispose();
        }
    }

    private void EnsureShellProcess()
    {
        if (Process.HasExited)
        {
            RestartShellProcess();
        }
    }
}
