using System.Text.Json.Serialization;

namespace LlmAgents.Configuration;

public class LlmApiConfig
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCompletionTokens { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; } = 1.0;

    [JsonPropertyName("llamacpp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LlamacppApiConfig? Llamacpp { get; set; }

    public bool Valid()
    {
        return !string.IsNullOrEmpty(ApiEndpoint) && !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(ApiModel) && ContextSize > 0;
    }
}
