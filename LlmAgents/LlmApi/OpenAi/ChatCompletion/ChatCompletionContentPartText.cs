namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

using System.Text.Json.Serialization;

public class ChatCompletionContentPartText : ChatCompletionContentPart
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";
}
