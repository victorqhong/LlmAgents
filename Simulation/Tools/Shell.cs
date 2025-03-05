namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;

public class Shell
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "shell",
            description = "Runs a shell command",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    command = new
                    {
                        type = "string",
                        description = "Shell command and arguments to run"
                    },
                    waitTimeMs = new
                    {
                        type = "string",
                        description = "Time in milliseconds to wait for the command to complete after which the command is killed (default is 10 seconds, 0 is immediately return, -1 is infinite wait)"
                    }
                },
                required = new[] { "command" }
            }
        }
    });

    public Shell()
    {
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
            var waitTimeMs = int.Parse(parameters["waitTimeMs"]?.ToString() ?? "10000");

            var process = new System.Diagnostics.Process();

            process.StartInfo.FileName = "pwsh";
            process.StartInfo.Arguments = "-NoLogo -NoProfile -NonInteractive -Command -";

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardInput = true;

            process.Start();

            process.StandardInput.WriteLine("$PSStyle.OutputRendering = \"PlainText\"");
            process.StandardInput.WriteLine(command);
            process.StandardInput.Flush();
            process.StandardInput.Close();

            process.WaitForExit(waitTimeMs);
            process.Kill(true);

            var output = process.StandardOutput.ReadToEnd();

            result.Add("stdout", output);
            result.Add("exitcode", process.ExitCode);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
