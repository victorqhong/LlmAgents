namespace LlmAgents.Tools.BackgroundJob;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class JobOutputTool : Tool
{
    private readonly JobManager jobManager;

    public JobOutputTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobManager = toolFactory.Resolve<JobManager>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new() 
        {
            Name = "job_output",
            Description = "Retrieve the captured stdout/stderr of a background job.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "job_id", new() { Type = "string", Description = "Identifier returned by start_job." } },
                    { "max_bytes", new() { Type = "integer", Description = "Maximum number of bytes to return (optional)." } },
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

        var output = info.Output.ToString();
        if (parameters.TryGetValueInt("max_bytes", out var maxBytes))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(output);
            if (bytes.Length > maxBytes)
            {
                output = System.Text.Encoding.UTF8.GetString(bytes, 0, maxBytes.Value);
            }
        }

        result.Add("output", output);

        return Task.FromResult<JsonNode>(result);
    }
}
