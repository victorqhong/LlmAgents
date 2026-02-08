using LlmAgents.LlmApi;
using LlmAgents.Tools;
using Newtonsoft.Json.Linq;
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

    public static LlmApiOpenAiParameters? InteractiveApiConfigSetup()
    {
        Console.WriteLine("Interactive API setup. Leave blank to cancel.");

        Console.Write("API endpoint (e.g. https://api.openai.com/v1/chat/completions): ");
        string? endpoint = Console.ReadLine();
        if (string.IsNullOrEmpty(endpoint))
        {
            return null;
        }

        Console.Write("API key: ");
        string? apiKey = Console.ReadLine();
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        Console.Write("Model name (e.g. gpt-3.5-turbo): ");
        string? model = Console.ReadLine();
        if (string.IsNullOrEmpty(model))
        {
            return null;
        }

        Console.Write("Context size: ");
        string? contextSizeInput = Console.ReadLine();
        if (string.IsNullOrEmpty(contextSizeInput) || int.TryParse(contextSizeInput, out var contextSize))
        {
            return null;
        }

        Console.Write("Max completion tokens: ");
        string? maxCompletionTokensInput = Console.ReadLine();
        if (string.IsNullOrEmpty(maxCompletionTokensInput) || int.TryParse(maxCompletionTokensInput, out var maxCompletionTokens))
        {
            return null;
        }

        var apiConfig = new JObject
        {
            ["apiEndpoint"] = endpoint,
            ["apiKey"] = apiKey,
            ["apiModel"] = model
        };

        EnsureConfigDirectoryExists();

        string configPath = Config.GetProfileConfig("api.json");
        File.WriteAllText(configPath, apiConfig.ToString());
        Console.WriteLine($"Saved API config to: {configPath}");

        return new LlmApiOpenAiParameters
        {
            ApiEndpoint = endpoint,
            ApiKey = apiKey,
            ApiModel = model,
            ContextSize = contextSize,
            MaxCompletionTokens = maxCompletionTokens,
        };
    }

    public static string? InteractiveToolsConfigSetup()
    {
        Console.WriteLine("Interactive tools config setup. Leave blank to cancel.");

        Console.Write("Path to tools assembly: ");
        var toolsAssembly = Console.ReadLine();
        if (string.IsNullOrEmpty(toolsAssembly))
        {
            return null;
        }

        var toolsConfig = ToolsConfigGenerator.GenerateToolConfig(toolsAssembly);
        if (toolsConfig == null)
        {
            return null;
        }

        EnsureConfigDirectoryExists();

        string configPath = GetProfileConfig("tools.json");
        File.WriteAllText(configPath, toolsConfig.ToString());
        Console.WriteLine($"Saved tools config to: {configPath}");

        Console.WriteLine("NOTE: Remember to add any necessary parameters to the generated config file.");

        return configPath;
    }
}
