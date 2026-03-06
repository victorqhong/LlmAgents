namespace AgentManager.Configuration;

public class AuthorizationOptions
{
    public const string SectionName = "Authorization";
    
    public bool RequireAuthorization { get; set; } = true;
    public AuthorizedUser[] AllowedUsers { get; set; } = Array.Empty<AuthorizedUser>();
}
