using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionResponseError
{
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    [JsonPropertyName("code")]
    public required int Code { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }
}
