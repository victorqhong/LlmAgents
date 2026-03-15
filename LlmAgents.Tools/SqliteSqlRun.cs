namespace LlmAgents.Tools;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class SqliteSqlRun : Tool
{
    public SqliteSqlRun(ToolFactory toolFactory)
        : base(toolFactory)
    {
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "sqlite_sql_run",
            Description = "Read/process sql statement against a sqlite database",
            Parameters = new()
            {
                Properties = new()
                {
                    { "sql", new() { Type = "string", Description = "SQL statement to run" } },
                    { "db", new() { Type = "string", Description = "Path to a sqlite database file" } }
                },
                Required = [ "sql", "db" ]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("sql", string.Empty, out var sql) || string.IsNullOrEmpty(sql))
        {
            result.Add("error", "sql parameter is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        if (!parameters.TryGetValueString("db", string.Empty, out var db) || string.IsNullOrEmpty(db))
        {
            result.Add("error", "db parameter is null or empty");
            return Task.FromResult<JsonNode>(result);
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

        return Task.FromResult<JsonNode>(result);
    }
}
