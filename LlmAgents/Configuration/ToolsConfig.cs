using System.Text.Json.Serialization;

namespace LlmAgents.Configuration;

public class ToolsConfig
{
    [JsonPropertyName("parameters")]
    public Dictionary<string, string>? Parameters { get; set; }

    [JsonPropertyName("types")]
    public List<string>? Types { get; set; }

    [JsonPropertyName("assemblies")]
    public Dictionary<string, string>? Assemblies { get; set; }
}

