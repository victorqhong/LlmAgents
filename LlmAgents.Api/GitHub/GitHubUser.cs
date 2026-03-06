namespace LlmAgents.Api.GitHub;

public record GitHubUser(
    long id,
    string login,
    string avatar_url
);
