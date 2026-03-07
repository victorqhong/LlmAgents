using Newtonsoft.Json;

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
            var token = JsonConvert.DeserializeObject<HubAuthToken>(json);
            
            // Check if token is expired
            if (token != null)
            {
                if (token.ExpireTime > DateTime.Now)
                {
                    // Token has expired, delete it
                    File.Delete(tokenPath);
                    return null;
                }
            }
            
            return token;
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

        var json = JsonConvert.SerializeObject(token, Formatting.Indented);
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
