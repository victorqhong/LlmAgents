using System.Text.Json;

namespace LlmAgents.Api;

public class HubAuthTokenStore
{
    private static readonly string ConfigFileName = "hub-auth.json";

    public static string GetTokenStorePath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".llmagents", ConfigFileName);
    }

    public static HubAuthToken? LoadToken()
    {
        var tokenPath = GetTokenStorePath();
        if (!File.Exists(tokenPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(tokenPath);
            return JsonSerializer.Deserialize<HubAuthToken>(json);
        }
        catch
        {
            // If there's any error reading the token, delete the file and return null
            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }

            return null;
        }
    }

    public static void SaveToken(HubAuthToken token)
    {
        var tokenPath = GetTokenStorePath();
        
        // Ensure the config directory exists
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configDir = Path.Combine(home, ".llmagents");
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        var json = JsonSerializer.Serialize(token);
        File.WriteAllText(tokenPath, json);
    }

    public static void ClearToken()
    {
        var tokenPath = GetTokenStorePath();
        if (File.Exists(tokenPath))
        {
            File.Delete(tokenPath);
        }
    }
}
