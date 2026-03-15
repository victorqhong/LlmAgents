using System.Text.Json;
using System.Text.Json.Nodes;

namespace LlmAgents.Tools;

public class ToolCallEvent : ToolEvent
{
    public required JsonDocument Arguments;
    public required JsonNode Result;
}
