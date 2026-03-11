using Newtonsoft.Json;

namespace LlmAgents.Api.GitHub;

public class TokenResponse
{
    [JsonProperty("access_token")]
    public required string AccessToken;

    [JsonProperty("token_type")]
    public required string TokenType;

    [JsonProperty("scope")]
    public required string Scope;
}
