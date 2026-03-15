using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi;

public class LlmApiOpenAiParameters
{
    [JsonPropertyName("apiEndpoint")]
    public required string ApiEndpoint { get; set; }

    [JsonPropertyName("apiKey")]
    public required string ApiKey { get; set; }

    [JsonPropertyName("apiModel")]
    public required string ApiModel { get; set; }

    [JsonPropertyName("contextSize")]
    public required int ContextSize { get; set; }

    [JsonPropertyName("maxCompletionTokens")]
    public int? MaxCompletionTokens { get; set; }

    public bool Valid()
    {
        return !string.IsNullOrEmpty(ApiEndpoint) && !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(ApiModel) && ContextSize > 0;
    }
}
