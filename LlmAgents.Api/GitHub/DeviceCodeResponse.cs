using Newtonsoft.Json;

namespace LlmAgents.Api.GitHub;

public class DeviceCodeResponse
{
    [JsonProperty("device_code")]
    public required string DeviceCode { get; set; }

    [JsonProperty("user_code")]
    public required string UserCode { get; set; }

    [JsonProperty("verification_uri")]
    public required string VerificationUri { get; set; }

    [JsonProperty("expires_in")]
    public required int ExpiresIn { get; set; }

    [JsonProperty("interval")]
    public required int Interval { get; set; }
}
