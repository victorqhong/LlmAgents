using Newtonsoft.Json.Linq;
using LlmAgents.State;

namespace LlmAgents.Tools.BackgroundJob;

public class StopJobTool : Tool
{
    private readonly JobManager jobManager;

    public StopJobTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobManager = toolFactory.Resolve<JobManager>();
        Schema = new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = "stop_job",
                ["description"] = "Cancel a running background job.",
                ["parameters"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["job_id"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Identifier returned by start_job."
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
        // Attempt to cancel the job. If the job does not exist, return an error.
        var info = jobManager.Get(jobId);
        if (info == null)
        {
            return Task.FromResult<JToken>(new JObject { ["error"] = "job not found" });
        }
        jobManager.Cancel(jobId);
        return Task.FromResult<JToken>(new JObject { ["result"] = "cancelled" });
    }
}
