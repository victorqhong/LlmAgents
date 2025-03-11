namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.Text;

public class Shell
{
    private readonly int waitTimeMs;
    private readonly string workingDirectory;

    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "shell",
            description = "Runs a shell command in PowerShell Core",
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

    public Shell(int waitTimeMs = 180000, string? workingDirectory = null)
    {
        this.waitTimeMs = waitTimeMs;
        this.workingDirectory = workingDirectory ?? Environment.CurrentDirectory;

        Tool = new Tool
        {
            Schema = schema,
            Function = Function
        };
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
            var process = new System.Diagnostics.Process();

            process.StartInfo.FileName = "pwsh";
            process.StartInfo.Arguments = $"-NoLogo -NonInteractive -Command -";
            process.StartInfo.WorkingDirectory = workingDirectory;

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;

            var stdout = new StringBuilder();
            process.OutputDataReceived += (sender, e) =>
            {
                Console.WriteLine(e.Data);
                stdout.AppendLine(e.Data);
            };

            var stderr = new StringBuilder();
            process.ErrorDataReceived += (sender, e) =>
            {
                Console.WriteLine(e.Data);
                stderr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.StandardInput.WriteLine("$PSStyle.OutputRendering = \"PlainText\"");
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
            process.StandardInput.Close();

            var checkTimeMs = 1000;
            var totalWaitTimeMs = 0;
            var exited = false;
            while (!exited)
            {
                if (totalWaitTimeMs >= waitTimeMs)
                {
                    break;
                }

                exited = process.WaitForExit(checkTimeMs);
                totalWaitTimeMs += checkTimeMs;
            }

            process.Kill(true);

            result.Add("exitcode", process.ExitCode);
            result.Add("exited", exited);
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
