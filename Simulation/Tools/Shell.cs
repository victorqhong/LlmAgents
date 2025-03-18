namespace Simulation.Tools;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

public class Shell
{
    private const int waitCheckTimeMs = 1000;

    private readonly ILogger log = Program.loggerFactory.CreateLogger(nameof(Shell));

    private readonly int waitTimeMs;

    private readonly StringBuilder stdout = new StringBuilder();
    private readonly StringBuilder stderr = new StringBuilder();

    private string? commandDelimiter;
    private bool receivedOutput = false;

    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "shell",
            description = "Runs a shell command in PowerShell Core. Prefer the 'file_write' tool for writing files.",
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

    public Process Process { get; private set; }

    public Shell(int waitTimeMs = 180000, string? workingDirectory = null)
    {
        this.waitTimeMs = waitTimeMs;

        Tool = new Tool
        {
            Schema = schema,
            Function = Function
        };

        Process = new Process();

        Process.StartInfo.FileName = "pwsh";
        Process.StartInfo.ArgumentList.Add("-NoLogo");
        Process.StartInfo.ArgumentList.Add("-NonInteractive");

        Process.StartInfo.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory; ;

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

            receivedOutput = !string.IsNullOrEmpty(commandDelimiter) && e.Data.Contains(commandDelimiter);
            if (receivedOutput)
            {
                return;
            }

            stdout.AppendLine(e.Data);
            log.LogInformation("{data}", e.Data);
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
            log.LogInformation("{data}", e.Data);
        };

        Process.Start();
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();

        receivedOutput = true;
        Process.StandardInput.WriteLine("$PSStyle.OutputRendering='Plain'");
    }

    public Tool Tool { get; private set; }

    private JObject Function(JObject parameters)
    {
        var result = new JObject();

        var command = parameters["command"]?.ToString();
        if (string.IsNullOrEmpty(command))
        {
            result.Add("error", "command is null or empty");
            return result;
        }

        try
        {
            var commandId = Guid.NewGuid().ToString();
            commandDelimiter = $"Write-Host \"{commandId}\"";

            receivedOutput = false;

            stdout.Clear();
            stderr.Clear();
            Process.StandardInput.WriteLine(command);
            Process.StandardInput.WriteLine(commandDelimiter);
            Process.StandardInput.Flush();

            var totalWaitTimeMs = 0;
            while (!receivedOutput)
            {
                if (totalWaitTimeMs >= waitTimeMs)
                {
                    log.LogInformation("shell command did not exit after {waitTimeMs} milliseconds", waitTimeMs);
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

        return result;
    }
}
