using Newtonsoft.Json.Linq;
using LlmAgents.State;

namespace LlmAgents.Tools.BackgroundJob;

public class JobOutputTool : Tool
{
    private readonly BackgroundJobStore jobStore;

    public JobOutputTool(ToolFactory toolFactory) : base(toolFactory)
    {
        jobStore = toolFactory.Resolve<BackgroundJobStore>();
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
                        },
                        ["cursor"] = new JObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Optional byte cursor to read from. If omitted, the durable cursor is used."
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
        if (!jobStore.TryGetOutputAndCursor(jobId, out var fullOutput, out var durableCursor))
        {
            return Task.FromResult<JToken>(new JObject { ["error"] = "job not found" });
        }

        var hasCursor = parameters["cursor"] != null;
        var cursor = durableCursor;
        if (hasCursor && !int.TryParse(parameters["cursor"]?.ToString(), out cursor))
        {
            return Task.FromResult<JToken>(new JObject { ["error"] = "invalid cursor" });
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(fullOutput);
        var start = Math.Clamp(cursor, 0, bytes.Length);
        var length = bytes.Length - start;
        if (parameters["max_bytes"] != null && int.TryParse(parameters["max_bytes"]?.ToString(), out var max))
        {
            length = Math.Min(length, Math.Max(0, max));
        }

        var output = length > 0
            ? System.Text.Encoding.UTF8.GetString(bytes, start, length)
            : string.Empty;
        var nextCursor = start + length;

        if (!hasCursor)
        {
            jobStore.SetCursor(jobId, nextCursor);
        }

        return Task.FromResult<JToken>(new JObject
        {
            ["output"] = output,
            ["cursor"] = start,
            ["next_cursor"] = nextCursor
        });
    }
}
