namespace AgentManager.Models.Containers;

using System.Text.Json.Serialization;

public class InstancesPost
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("source")]
    public required InstanceSource Source { get; set; }

    [JsonPropertyName("start")]
    public required bool Start { get; set; }
}

public class InstanceSource
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("protocol")]
    public required string Protocol { get; set; }

    [JsonPropertyName("server")]
    public required string Server { get; set; }

    [JsonPropertyName("alias")]
    public required string Alias { get; set; }
}
