using System.Text.Json.Serialization;

namespace LlmAgents.Api.GitHub;

public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")]
    public required string DeviceCode { get; set; }

    [JsonPropertyName("user_code")]
    public required string UserCode { get; set; }

    [JsonPropertyName("verification_uri")]
    public required string VerificationUri { get; set; }

    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; set; }

    [JsonPropertyName("interval")]
    public required int Interval { get; set; }
}
