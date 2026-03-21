
using System.Text.Json.Serialization;
using LlmAgents.LlmApi.Llamacpp;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

[JsonDerivedType(typeof(LlamacppChatCompletionRequest))]
public class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<ChatCompletionMessageParam> Messages { get; set; }

    [JsonPropertyName("max_completion_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCompletionTokens { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChatCompletionFunctionTool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolChoice { get; set; }

    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }

    [JsonPropertyName("stream_options")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, dynamic>? StreamOptions { get; set; }

    public ChatCompletionRequest()
    {
    }

    public ChatCompletionRequest(bool? stream)
    {
        Stream = stream;
        if (stream.HasValue && stream.Value)
        {
            StreamOptions = new Dictionary<string, dynamic>()
            {
                { "include_usage", true }
            };
        }
    }
}
