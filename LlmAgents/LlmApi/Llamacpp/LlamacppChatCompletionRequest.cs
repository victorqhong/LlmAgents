using System.Text.Json.Serialization;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;

namespace LlmAgents.LlmApi.Llamacpp;

public class LlamacppChatCompletionRequest : ChatCompletionRequest
{
    [JsonPropertyName("id_slot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SlotId { get; set; } = -1;

    public LlamacppChatCompletionRequest() { }
    public LlamacppChatCompletionRequest(bool stream) : base(stream) { }
}
