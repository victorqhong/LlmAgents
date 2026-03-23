using System.Text.Json.Serialization;

namespace LlmAgents.Configuration;

public class McpConfig
{
    [JsonPropertyName("servers")]
    public required Dictionary<string, IMcpServerConfig> Servers { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(McpServerConfigHttp), "http")]
[JsonDerivedType(typeof(McpServerConfigStdio), "stdio")]
public interface IMcpServerConfig { }

public class McpServerConfigHttp : IMcpServerConfig
{
    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
}

public class McpServerConfigStdio : IMcpServerConfig
{
    [JsonPropertyName("command")]
    public required string Command { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }
}
