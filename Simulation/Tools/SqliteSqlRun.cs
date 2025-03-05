namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;

public class SqliteSqlRun
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "sqlite_sql_run",
            description = "Read/process sql statement against a sqlite database",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    sql = new
                    {
                        type = "string",
                        description = "SQL statement to run"
                    },
                    db = new
                    {
                        type = "string",
                        description = "Path to a sqlite database file"
                    }
                },
                required = new[] { "sql", "db" }
            }
        }
    });

    public SqliteSqlRun()
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

        var sql = parameters["sql"]?.ToString();
        if (string.IsNullOrEmpty(sql))
        {
            result.Add("error", "sql parameter is null or empty");
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
            process.StartInfo.FileName = "sqlite3";
            process.StartInfo.Arguments = $"{db} {sql.Replace("\n", Environment.NewLine)}";
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
