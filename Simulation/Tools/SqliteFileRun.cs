namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;

public class SqliteFileRun
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "sqlite_file_run",
            description = "Read/process named file against a sqlite database",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    file = new
                    {
                        type = "string",
                        description = "Path to a file containing sqlite commands"
                    },
                    db = new
                    {
                        type = "string",
                        description = "Path to a sqlite database file"
                    }
                },
                required = new[] { "file", "db" }
            }
        }
    });

    public SqliteFileRun()
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

        var file = parameters["file"]?.ToString();
        if (string.IsNullOrEmpty(file))
        {
            result.Add("error", "file parameter is null or empty");
            return result;
        }

        var db = parameters["db"]?.ToString();
        if (string.IsNullOrEmpty(db))
        {
            result.Add("error", "db parameter is null or empty");
            return result;
        }

        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "sqlite";
            process.StartInfo.Arguments = $"-init {file} {db}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardInput = true;
            process.Start();
            process.StandardInput.WriteLine(".quit");
            process.WaitForExit();
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
