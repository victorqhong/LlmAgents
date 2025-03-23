namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;

public class SqliteFileRun : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    public SqliteFileRun(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
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

    public override JObject Function(JObject parameters)
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
            if (restrictToBasePath && !Path.IsPathRooted(file))
            {
                file = Path.Combine(basePath, file);
            }
            else
            {
                file = Path.GetFullPath(file);
            }

            if (restrictToBasePath && !file.StartsWith(basePath))
            {
                result.Add("error", $"files outside {basePath} can not be read");
                return result;
            }

            var process = new System.Diagnostics.Process();
            process.StartInfo.WorkingDirectory = restrictToBasePath ? basePath : Environment.CurrentDirectory;
            process.StartInfo.FileName = "sqlite3";
            process.StartInfo.Arguments = $"-init {file} {db}";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.StandardInput.WriteLine(".quit");
            process.WaitForExit();

            result.Add("stdout", process.StandardOutput.ReadToEnd());
            result.Add("stderr", process.StandardError.ReadToEnd());
            result.Add("exitcode", process.ExitCode);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
