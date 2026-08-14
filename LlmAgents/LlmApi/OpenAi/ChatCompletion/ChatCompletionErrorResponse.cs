using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionErrorResponse
{
    [JsonPropertyName("error")]
    public ChatCompletionResponseError? Error { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
