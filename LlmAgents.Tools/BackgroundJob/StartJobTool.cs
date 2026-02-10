using Newtonsoft.Json.Linq;
using LlmAgents.State;

namespace LlmAgents.Tools.BackgroundJob;

public class StartJobTool : Tool
{
    private readonly JobManager jobManager;

    public StartJobTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobManager = toolFactory.Resolve<JobManager>();
        Schema = new JObject
        {
            ["type"] = "function",
            ["function"] = new JObject
            {
                ["name"] = "start_job",
                ["description"] = "Start a background process and get a job identifier.",
                ["parameters"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["command"] = new JObject
                        {
                            ["type"] = "string",
                            ["description"] = "Executable to run (full path or on PATH)."
                        },
                        ["args"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject { ["type"] = "string" },
                            ["description"] = "Arguments to pass to the command."
                        }
                    },
                    ["required"] = new JArray { "command" }
                }
            }
        };
    }

    public override JObject Schema { get; protected set; }

    public override Task<JToken> Function(Session session, JObject parameters)
    {
        var command = parameters["command"]?.ToString();
        var argsArray = parameters["args"] as JArray;
        var args = argsArray?.Select(t => t.ToString()).ToArray() ?? Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.FromResult<JToken>(new JObject { ["error"] = "command missing" });
        }
        var jobId = jobManager.Start(command, args);
        return Task.FromResult<JToken>(new JObject { ["job_id"] = jobId.ToString() });
    }
}
