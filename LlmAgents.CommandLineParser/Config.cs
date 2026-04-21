using System.Runtime.InteropServices;

namespace LlmAgents.CommandLineParser;

public static class Config
{
    public static string GetProfileConfig(string file)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".llmagents", file);
    }

    public static string? GetConfigFile(string fileName, string environmentVariableName)
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

        var environmentVariable = GetConfigEnvironmentVariable(environmentVariableName);
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

    public static string? GetConfigEnvironmentVariable(string environmentVariableName)
    {
        var environmentVariableTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;
        return Environment.GetEnvironmentVariable(environmentVariableName, environmentVariableTarget);
    }
}
