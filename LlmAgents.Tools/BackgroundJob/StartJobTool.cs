namespace LlmAgents.Tools.BackgroundJob;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class StartJobTool : Tool
{
    private readonly JobManager jobManager;

    public StartJobTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobManager = toolFactory.Resolve<JobManager>();
        Schema = new() 
        {
            Function = new()
            {
                Name = "start_job",
                Description = "Start a background process and get a job identifier.",
                Parameters = new()
                {
                    Properties = new()
                    {
                        { "command", new() { Type = "string", Description = "Executable to run (full path or on PATH)." } },
                        { "args", new() { Type = "array", Description = "Arguments to pass to the command.", Items = new() { Type = "string" } } },
                    },
                    Required = ["command"]
                }
            }
        };
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; }

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("command", string.Empty, out var command) || string.IsNullOrEmpty(command))
        {
            result.Add("error", "command is null or empty");
            return Task.FromResult<JsonNode>(result);
            
        }

        // var argsArray = parameters["args"] as JArray;
        var argsArray = parameters.RootElement.GetProperty("args").EnumerateArray();
        var args = argsArray.Select(t => t.ToString()).ToArray() ?? Array.Empty<string>();

        var jobId = jobManager.Start(command, args);
        result.Add("job_id", jobId.ToString());

        return Task.FromResult<JsonNode>(result);
    }
}
