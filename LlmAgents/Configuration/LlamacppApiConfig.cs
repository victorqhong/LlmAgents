using System.Text.Json.Serialization;

namespace LlmAgents.Configuration;

public class LlamacppApiConfig
{
    [JsonPropertyName("slotId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SlotId { get; set; } = -1;
}
