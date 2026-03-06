namespace LlmAgents.Api.GitHub;

using System.Net.Http.Headers;
using LlmAgents.Communication;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class Login
{
    private const string GITHUB_OAUTH_CLIENTID = "Ov23li78gAClm6qtZPFH";

    public static async Task<string?> GetHubLoginToken(IAgentCommunication communication, Uri agentHubUri)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var deviceCodeResponse = await client.PostAsync("https://github.com/login/device/code", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = GITHUB_OAUTH_CLIENTID,
            ["scope"] = "read:user user:email"
        }));

        if (!deviceCodeResponse.IsSuccessStatusCode)
        {
            return null;
        }

        var deviceCodeContent = await deviceCodeResponse.Content.ReadAsStringAsync();

        var deviceCode = JsonConvert.DeserializeObject<DeviceCodeResponse>(deviceCodeContent);
        if (deviceCode == null || string.IsNullOrEmpty(deviceCode.DeviceCode))
        {
            return null;
        }

        await communication.SendMessage($"Go to {deviceCode.VerificationUri}", true);
        await communication.SendMessage($"Enter code: {deviceCode.UserCode}", true);

        var elapsed = 0;
        TokenResponse? tokenResponse = null;
        while (tokenResponse == null && elapsed < deviceCode.ExpiresIn)
        {
            await Task.Delay(deviceCode.Interval * 1000);
            elapsed += deviceCode.Interval;

            var tokenResult = await client.PostAsync("https://github.com/login/oauth/access_token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = GITHUB_OAUTH_CLIENTID,
                ["device_code"] = deviceCode.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }));

            var payload = await tokenResult.Content.ReadAsStringAsync();
            if (payload.Contains("authorization_pending"))
                continue;

            tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(payload);
        }

        if (tokenResponse == null)
        {
            return null;
        }

        var response = await client.GetAsync($"{agentHubUri}auth/github?accessToken={tokenResponse.AccessToken}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var apiContent = await response.Content.ReadAsStringAsync();
        var json = JObject.Parse(apiContent);

        return json.Value<string>("accessToken");
    }
}
