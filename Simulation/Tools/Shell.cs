namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

public class Shell
{
    public static Newtonsoft.Json.Linq.JObject Definition;

    public static System.Func<JObject, JObject> Function = (parameters) =>
    {
        var command = parameters["command"]?.ToString();
        if (string.IsNullOrEmpty(command))
        {
            return new JObject();
        }

        var commandParts = command.Split(' ');
        var fileName = commandParts[0];
        var arguments = commandParts.AsSpan(1).ToArray();

        var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = string.Join(" ", arguments);
        process.StartInfo.RedirectStandardOutput = true;
        process.Start();
        process.WaitForExit();
        var output = process.StandardOutput.ReadToEnd();

        var result = new JObject();
        result.Add("stdout", output);
        result.Add("exitcode", process.ExitCode);

        return result;
    };

    static Shell()
    {
        Definition = Newtonsoft.Json.Linq.JObject.FromObject(new
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
                        }
                    },
                    required = new[] { "command" }
                }
            }
        });
    }
}

