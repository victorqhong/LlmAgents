using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionMessageParamTool : ChatCompletionMessageParam
{
    [JsonPropertyName("tool_call_id")]
    public required string ToolCallId { get; set; }

    public ChatCompletionMessageParamTool()
    {
        Role = "tool";
    }
}
