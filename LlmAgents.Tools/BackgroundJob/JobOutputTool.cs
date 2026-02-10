using Newtonsoft.Json.Linq;
using LlmAgents.State;

namespace LlmAgents.Tools.BackgroundJob;

public class JobOutputTool : Tool
{
    private readonly JobManager jobManager;

    public JobOutputTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobManager = toolFactory.Resolve<JobManager>();
        Schema = new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = "job_output",
                ["description"] = "Retrieve the captured stdout/stderr of a background job.",
                ["parameters"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["job_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identifier returned by start_job."
                        },
                        ["max_bytes"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of bytes to return (optional)."
                        }
                    },
                    ["required"] = new JArray { "job_id" }
                }
            }
        };
    }

    public override JObject Schema { get; protected set; }

    public override Task<JToken> Function(Session session, JObject parameters)
    {
        var jobIdStr = parameters["job_id"]?.ToString();
        if (!Guid.TryParse(jobIdStr, out var jobId))
        {
            return Task.FromResult<JToken>(new JObject { ["error"] = "invalid job_id" });
        }
        var info = jobManager.Get(jobId);
        if (info == null)
        {
            return Task.FromResult<JToken>(new JObject { ["error"] = "job not found" });
        }
        var output = info.Output.ToString();
        if (parameters["max_bytes"] != null && int.TryParse(parameters["max_bytes"]?.ToString(), out var max))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(output);
            if (bytes.Length > max)
            {
                output = System.Text.Encoding.UTF8.GetString(bytes, 0, max);
            }
        }
        return Task.FromResult<JToken>(new JObject { ["output"] = output });
    }
}
