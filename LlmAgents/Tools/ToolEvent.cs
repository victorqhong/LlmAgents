using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class ToolEvent
{
    public required Tool Sender;
    public required JObject Arguments;
    public required JToken Result;
}
