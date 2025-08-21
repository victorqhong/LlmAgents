using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class ToolCallEvent : ToolEvent
{
    public required JObject Arguments;
    public required JToken Result;
}
