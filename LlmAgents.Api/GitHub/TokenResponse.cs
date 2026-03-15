using System.Text.Json.Serialization;

namespace LlmAgents.Api.GitHub;

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; set; }

    [JsonPropertyName("scope")]
    public required string Scope { get; set; }
}
