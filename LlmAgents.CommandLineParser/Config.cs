using System.Runtime.InteropServices;

namespace LlmAgents.CommandLineParser;

public static class Config
{
    public static string GetProfileConfig(string file)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".llmagents", file);
    }

    public static string? GetConfigOptionDefaultValue(string fileName, string environmentVariableName)
    {
        if (File.Exists(fileName))
        {
            return fileName;
        }

        var profileConfig = GetProfileConfig(fileName);
        if (File.Exists(profileConfig))
        {
            return profileConfig;
        }

        var environmentVariableTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;
        var environmentVariable = Environment.GetEnvironmentVariable(environmentVariableName, environmentVariableTarget);
        if (File.Exists(environmentVariable))
        {
            return environmentVariable;
        }

        return null;
    }

    public static void EnsureConfigDirectoryExists()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string configDir = Path.Combine(home, ".llmagents");
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
    }
}
