namespace LlmAgents.Tools;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public class Shell : Tool
{
    private const int waitCheckTimeMs = 1000;

    private static string shellName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PowerShell Core" : "bash";

    private static string echoCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Write-Host" : "echo";

    private readonly ILogger Log;

    private readonly int waitTimeMs;

    private readonly StringBuilder stdout = new StringBuilder();
    private readonly StringBuilder stderr = new StringBuilder();

    private string? commandId;
    private bool receivedOutput = false;

    private string currentDirectory;

    public Shell(ToolFactory toolFactory)
        : base(toolFactory)
    {
        Log = toolFactory.Resolve<ILoggerFactory>().CreateLogger(nameof(Shell));

        waitTimeMs = int.TryParse(toolFactory.GetParameter($"{nameof(Shell)}.{nameof(waitTimeMs)}"), out waitTimeMs) ? waitTimeMs : 180000;

        currentDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;

        Process = new Process();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.StartInfo.FileName = "pwsh";
            Process.StartInfo.ArgumentList.Add("-NoLogo");
            Process.StartInfo.ArgumentList.Add("-NonInteractive");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.StartInfo.FileName = "bash";
        }
        else
        {
            throw new NotImplementedException(RuntimeInformation.RuntimeIdentifier);
        }

        Process.StartInfo.WorkingDirectory = Path.IsPathFullyQualified(currentDirectory) ? currentDirectory : Path.Combine(Environment.CurrentDirectory, currentDirectory);

        Process.StartInfo.UseShellExecute = false;
        Process.StartInfo.RedirectStandardOutput = true;
        Process.StartInfo.RedirectStandardInput = true;
        Process.StartInfo.RedirectStandardError = true;
        Process.EnableRaisingEvents = true;

        Process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            if (string.IsNullOrEmpty(commandId))
            {
                return;
            }

            receivedOutput = e.Data.Contains(commandId);
            if (receivedOutput)
            {
                return;
            }

            stdout.AppendLine(e.Data);
            Log.LogInformation("{data}", e.Data);
        };

        Process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            if (receivedOutput)
            {
                return;
            }

            stderr.AppendLine(e.Data);
            Log.LogInformation("{data}", e.Data);
        };

        Process.Start();
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();

        receivedOutput = true;
        Process.StandardInput.WriteLine("$PSStyle.OutputRendering='Plain'");

        var toolEventBus = toolFactory.Resolve<IToolEventBus>();
        toolEventBus.SubscribeToolEvent<DirectoryChange>(OnChangeDirectory);
    }

    public Process Process { get; private set; }

    public CancellationTokenSource CancellationTokenSource { get; private set; } = new();

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "shell",
            description = $"Runs a shell command in {shellName}. Prefer the 'file_write' tool for writing files.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    command = new
                    {
                        type = "string",
                        description = "Shell command and arguments to run"
                    }
                },
                required = new[] { "command" }
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var command = parameters["command"]?.ToString();
        if (string.IsNullOrEmpty(command))
        {
            result.Add("error", "command is null or empty");
            return Task.FromResult<JToken>(result);
        }

        try
        {
            commandId = Guid.NewGuid().ToString();
            var commandDelimiter = $"{echoCommand} \"{commandId}\"";

            CancellationTokenSource = new CancellationTokenSource();

            receivedOutput = false;

            stdout.Clear();
            stderr.Clear();
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
            }

            result.Add("stdout", stdout.ToString());
            result.Add("stderr", stderr.ToString());
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }

    private Task OnChangeDirectory(ToolEvent e)
    {
        if (e is ToolCallEvent tce)
        {
            currentDirectory = tce.Result.Value<string>("currentDirectory") ?? currentDirectory;
        }
        else if (e is Events.ChangeDirectoryEvent cde)
        {
            currentDirectory = cde.Directory;
        }

        Process.StandardInput.WriteLine($"cd {currentDirectory}");

        return Task.CompletedTask;
    }
}
