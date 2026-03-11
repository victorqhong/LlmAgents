namespace AgentManager.Models.Containers;

using System.Text.Json.Serialization;

public class InstanceStatePut
{
    [JsonPropertyName("action")]
    public required string Action { get; set; }

    [JsonPropertyName("force")]
    public bool Force { get; set; } = false;

    [JsonPropertyName("stateful")]
    public bool? Stateful { get; set; }

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 30;
}
