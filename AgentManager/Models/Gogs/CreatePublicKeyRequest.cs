using System.Text.Json.Serialization;

namespace AgentManager.Models.Gogs;

public class CreatePublicKeyRequest
{
    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("key")]
    public required string Key { get; set; }
}
