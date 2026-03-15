using System.Text.Json.Serialization;

namespace LlmAgents.Configuration;

public class McpConfig
{
    [JsonPropertyName("servers")]
    public required Dictionary<string, IMcpServerConfig> Servers { get; set; }
}

[JsonDerivedType(typeof(McpServerConfigHttp))]
[JsonDerivedType(typeof(McpServerConfigStdio))]
public interface IMcpServerConfig { }

public class McpServerConfigHttp
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}

public class McpServerConfigStdio
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("command")]
    public required string Command { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }
}
