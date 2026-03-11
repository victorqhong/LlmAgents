namespace LlmAgents.Api;

public class HubAuthToken
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public required int ExpiresIn { get; set; }
    public required DateTime ExpireTime { get; set; }
}
