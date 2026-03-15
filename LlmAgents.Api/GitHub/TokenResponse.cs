using System.Text.Json.Serialization;

namespace LlmAgents.Api.GitHub;

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken;

    [JsonPropertyName("token_type")]
    public required string TokenType;

    [JsonPropertyName("scope")]
    public required string Scope;
}
