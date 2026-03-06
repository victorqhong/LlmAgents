using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security;

namespace LlmAgents.Api.GitHub;

public static class GitHubApi
{
    public static async Task<GitHubUser?> GetUserAsync(string accessToken)
    {
        using var client = new HttpClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AgentManager");

        var response = await client.GetAsync("https://api.github.com/user");

        if (!response.IsSuccessStatusCode)
            throw new SecurityException("Invalid GitHub access token");

        return await response.Content.ReadFromJsonAsync<GitHubUser>();
    }
}
