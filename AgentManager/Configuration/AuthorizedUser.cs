namespace AgentManager.Configuration;

public class AuthorizedUser
{
    public string GitHubLogin { get; set; } = string.Empty;
    public string[] Roles { get; set; } = Array.Empty<string>();
}

