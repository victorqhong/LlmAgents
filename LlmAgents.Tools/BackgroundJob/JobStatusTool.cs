using Newtonsoft.Json.Linq;
using LlmAgents.State;

namespace LlmAgents.Tools.BackgroundJob;

public class JobStatusTool : Tool
{
    private readonly JobManager jobManager;

    public JobStatusTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobManager = toolFactory.Resolve<JobManager>();
        Schema = new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = "job_status",
                ["description"] = "Get the current status of a background job.",
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
        var info = jobManager.Get(jobId);
        if (info == null)
        {
            return Task.FromResult<JToken>(new JObject { ["error"] = "job not found" });
        }
        var result = new JObject
        {
            ["status"] = info.Status.ToString().ToLowerInvariant(),
            ["started"] = info.Started,
            ["ended"] = info.Ended,
            ["exit_code"] = info.ExitCode
        };
        return Task.FromResult<JToken>(result);
    }
}
