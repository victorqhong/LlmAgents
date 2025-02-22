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
                    wait_for_exit = new
                    {
                        type = "string",
                        description = "'true' or 'false' whether to wait for the command to exit default is 'true' (optional)"
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
            var waitForExit = bool.Parse(parameters["wait_for_exit"]?.ToString() ?? "true");
            var commandParts = command.Split(' ');
            var fileName = commandParts[0];
            var arguments = commandParts.AsSpan(1).ToArray();

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = string.Join(" ", arguments);
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();
            if (waitForExit)
            {
                process.WaitForExit();
            }
            var output = process.StandardOutput.ReadToEnd();

            result.Add("pid", process.Id);
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
