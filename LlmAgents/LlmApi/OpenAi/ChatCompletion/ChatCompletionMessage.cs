namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

using System.Text.Json.Serialization;

public class ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ChatCompletionMessageFunctionToolCall>? ToolCalls { get; set; }
}
