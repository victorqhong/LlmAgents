using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionErrorResponse
{
    [JsonPropertyName("error")]
    public required ChatCompletionResponseError Error { get; set; }
}
