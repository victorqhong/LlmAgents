namespace LlmAgents.Tools.BackgroundJob;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class StopJobTool : Tool
{
    private readonly JobManager jobManager;

    public StopJobTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobManager = toolFactory.Resolve<JobManager>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new() 
    {
        Function = new()
        {
            Name = "stop_job",
            Description = "Cancel a running background job.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "job_id", new() { Type = "string", Description = "Identifier returned by start_job." } },
                },
                Required = ["job_id"]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("job_id", string.Empty, out var jobIdStr) || !Guid.TryParse(jobIdStr, out var jobId))
        {
            result.Add("error", "invalid job_id");
            return Task.FromResult<JsonNode>(result);
        }

        var info = jobManager.Get(jobId);
        if (info == null)
        {
            result.Add("error", "job not found");
            return Task.FromResult<JsonNode>(result);
        }

        jobManager.Cancel(jobId);
        result.Add("result", "cancelled");

        return Task.FromResult<JsonNode>(result);
    }
}
