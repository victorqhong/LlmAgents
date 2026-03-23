using System.Text.Json.Serialization;

namespace AgentManager.Models.Gogs;

public class CreatePublicKeyResponse
{
    [JsonPropertyName("id")]
    public required int Id { get; set; }

    [JsonPropertyName("key")]
    public required string Key { get; set; }

    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("title")]
    public required string Title { get; set; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; set; }
}
