using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionMessageParamAssistant : ChatCompletionMessageParam
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChatCompletionMessageFunctionToolCall>? ToolCalls { get; set; }

    public ChatCompletionMessageParamAssistant() { }
    public ChatCompletionMessageParamAssistant(string content) { Content = new ChatCompletionMessageParamContentString { Content = content }; }
}
