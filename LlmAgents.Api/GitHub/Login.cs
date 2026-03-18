namespace LlmAgents.Api.GitHub;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LlmAgents.Api;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;

public static class Login
{
    private const string GITHUB_OAUTH_CLIENTID = "Ov23li78gAClm6qtZPFH";

    public static async Task<string?> GetHubLoginToken(IAgentCommunication communication, Uri agentHubUri, CancellationToken cancellationToken)
    {
        var storedToken = HubAuthTokenStore.LoadToken();
        if (storedToken == null)
        {
            return await PerformOAuthFlow(communication, agentHubUri, cancellationToken);
        }

        var remainingTime = storedToken.ExpireTime - DateTime.Now;
        if (remainingTime > TimeSpan.FromMinutes(storedToken.ExpiresIn / 2.0))
        {
           return storedToken.AccessToken; 
        }

        var accessToken = await RefreshHubLoginToken(agentHubUri, cancellationToken);
        if (accessToken != null)
        {
            return accessToken;
        }

        return await PerformOAuthFlow(communication, agentHubUri, cancellationToken);
    }

    public static async Task<string?> RefreshHubLoginToken(Uri agentHubUri, CancellationToken cancellationToken)
    {
        var storedToken = HubAuthTokenStore.LoadToken();
        if (storedToken == null || string.IsNullOrEmpty(storedToken.RefreshToken))
        {
            return null;
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var content = JsonContent.Create(new RefreshRequest { RefreshToken = storedToken.RefreshToken });
        var response = await client.PostAsync($"{agentHubUri}auth/refresh", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var hubAuthToken = await response.Content.ReadFromJsonAsync<HubAuthToken>(cancellationToken);
        if (hubAuthToken == null)
        {
            return null;
        }

        HubAuthTokenStore.SaveToken(hubAuthToken);

        return hubAuthToken.AccessToken;
    }

    public static async Task<string?> PerformOAuthFlow(IAgentCommunication communication, Uri agentHubUri, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var deviceCodeResponse = await client.PostAsync("https://github.com/login/device/code", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GITHUB_OAUTH_CLIENTID,
            ["scope"] = "read:user user:email"
        }), cancellationToken);

        if (!deviceCodeResponse.IsSuccessStatusCode)
        {
            return null;
        }

        var deviceCodeContent = await deviceCodeResponse.Content.ReadAsStringAsync(cancellationToken);

        var deviceCode = JsonSerializer.Deserialize<DeviceCodeResponse>(deviceCodeContent);
        if (deviceCode == null || string.IsNullOrEmpty(deviceCode.DeviceCode))
        {
            return null;
        }

        bool? startAuthentication = false;
        await communication.SendMessage("Logging in to AgentManager with GitHub. Continue? (y/n): ", false);
        var messageContents = await communication.WaitForContent(cancellationToken);
        if (messageContents == null)
        {
            return null;
        }

        foreach (var c in messageContents)
        {
            if (c is not MessageContentText textContent)
            {
                continue;
            }

            startAuthentication = string.Equals("y", textContent.Text);
        }

        if (startAuthentication == null || !startAuthentication.Value)
        {
            return null;
        }

        await communication.SendMessage($"Go to {deviceCode.VerificationUri}", true);
        await communication.SendMessage($"Enter code: {deviceCode.UserCode}", true);

        var elapsed = 0;
        TokenResponse? tokenResponse = null;
        while (tokenResponse == null && elapsed < deviceCode.ExpiresIn && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(deviceCode.Interval * 1000, cancellationToken);
            elapsed += deviceCode.Interval;

            var tokenResult = await client.PostAsync("https://github.com/login/oauth/access_token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = GITHUB_OAUTH_CLIENTID,
                ["device_code"] = deviceCode.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }), cancellationToken);

            var payload = await tokenResult.Content.ReadAsStringAsync(cancellationToken);
            if (payload.Contains("authorization_pending"))
                continue;

            tokenResponse = JsonSerializer.Deserialize<TokenResponse>(payload);
        }

        if (tokenResponse == null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            return null;
        }

        var content = JsonContent.Create(new TokenRequest { AccessToken = tokenResponse.AccessToken });
        var response = await client.PostAsync($"{agentHubUri}auth/github", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var hubAuthToken = await response.Content.ReadFromJsonAsync<HubAuthToken>(cancellationToken);
        if (hubAuthToken == null)
        {
            return null;
        }

        HubAuthTokenStore.SaveToken(hubAuthToken);

        return hubAuthToken.AccessToken;
    }
}
