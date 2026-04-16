namespace LlmAgents.Tools;

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

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

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "sqlite_file_run",
            Description = "Read/process named file against a sqlite database",
            Parameters = new()
            {
                Properties = new()
                {
                    { "file", new() { Type = "string", Description = "Path to a file containing sqlite commands"  } },
                    { "db", new() { Type = "string", Description = "Path to a sqlite database file" } },
                },
                Required = [ "file", "db" ]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("file", string.Empty, out var file) || string.IsNullOrEmpty(file))
        {
            result.Add("error", "file parameter is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        if (!parameters.TryGetValueString("db", string.Empty, out var db) || string.IsNullOrEmpty(db))
        {
            result.Add("error", "db parameter is null or empty");
            return Task.FromResult<JsonNode>(result);
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
                return Task.FromResult<JsonNode>(result);
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

        return Task.FromResult<JsonNode>(result);
    }
}
