namespace LlmAgents.Api.GitHub;

using System.Net.Http.Headers;
using LlmAgents.Api;
using LlmAgents.Communication;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        var remainingTime = DateTime.Now - storedToken.ExpireTime;
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

        var response = await client.GetAsync($"{agentHubUri}auth/refresh?refreshToken={storedToken.RefreshToken}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            HubAuthTokenStore.ClearToken();
            return null;
        }

        var apiContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JObject.Parse(apiContent);

        var hubAuthToken = new HubAuthToken
        {
            AccessToken = json.Value<string>("accessToken") ?? string.Empty,
            RefreshToken = json.Value<string>("newRefreshToken") ?? string.Empty,
            ExpiresIn = json.Value<int>("expiresIn"),
            ExpireTime = DateTime.Now.AddMinutes(json.Value<int>("expiresIn"))
        };

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

        var deviceCode = JsonConvert.DeserializeObject<DeviceCodeResponse>(deviceCodeContent);
        if (deviceCode == null || string.IsNullOrEmpty(deviceCode.DeviceCode))
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

            tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(payload);
        }

        if (tokenResponse == null)
        {
            return null;
        }

        var response = await client.GetAsync($"{agentHubUri}auth/github?accessToken={tokenResponse.AccessToken}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var apiContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JObject.Parse(apiContent);

        var hubAuthToken = new HubAuthToken
        {
            AccessToken = json.Value<string>("accessToken") ?? string.Empty,
            RefreshToken = json.Value<string>("refreshToken") ?? string.Empty,
            ExpiresIn = json.Value<int>("expiresIn"),
            ExpireTime = DateTime.Now.AddMinutes(json.Value<int>("expiresIn"))
        };

        HubAuthTokenStore.SaveToken(hubAuthToken);

        return hubAuthToken.AccessToken;
    }
}
