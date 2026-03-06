namespace AgentManager.Models.Containers;

using System.Text.Json.Serialization;

public class InstanceExecPost
{
    [JsonPropertyName("command")]
    public required List<string> Command { get; set; }

    [JsonPropertyName("cwd")]
    public required string CurrentWorkingDirectory { get; set; }

    [JsonPropertyName("environment")]
    public required Dictionary<string, string> Environment { get; set; }

    [JsonPropertyName("user")]
    public int User { get; set; }

    [JsonPropertyName("group")]
    public int Group { get; set; }

    [JsonPropertyName("interactive")]
    public bool Interactive { get; set; }

    [JsonPropertyName("record-output")]
    public bool RecordOutput { get; set; }

    [JsonPropertyName("wait-for-websocket")]
    public bool WaitForWebsocket { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}

