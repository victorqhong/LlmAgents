using System.CommandLine;

namespace ConsoleAgent;

internal static class Options
{
    public readonly static Option<string> AgentId = new Option<string>(
        name: "--agentId",
        description: "Value used to identify the agent");

    public readonly static Option<string> ApiEndpoint = new Option<string>(
        name: "--apiEndpoint",
        description: "HTTP(s) endpoint of OpenAI compatible API");

    public readonly static Option<string> ApiKey = new Option<string>(
        name: "--apiKey",
        description: "Key used to authenticate to the api");

    public readonly static Option<string> ApiModel = new Option<string>(
        name: "--apiModel",
        description: "Name of the model to include in requests");

    public readonly static Option<string> ApiConfig = new Option<string>(
        name: "--apiConfig",
        description: "Path to a JSON file with configuration for api values",
        getDefaultValue: () => Config.GetConfigOptionDefaultValue("api.json", "LLMAGENTS_API_CONFIG"));

    public readonly static Option<bool> Persistent = new Option<bool>(
        name: "--persistent",
        description: "Whether messages are saved",
        getDefaultValue: () => false);

    public readonly static Option<string> SystemPromptFile = new Option<string>(
        name: "--systemPromptFile",
        description: "The path to a file containing the system prompt text. Option has no effect if messages are loaded from a previous persistent session.",
        getDefaultValue: () => "");

    public readonly static Option<string> WorkingDirectory = new Option<string>(
        name: "--workingDirectory",
        description: "Directory which agent has access to by default",
        getDefaultValue: () => Environment.CurrentDirectory);

    public readonly static Option<string> StorageDirectory = new Option<string>(
        name: "--storageDirectory",
        description: "Directory used to store agent related data",
        getDefaultValue: () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlmAgents"));

    public readonly static Option<string> SessionId = new Option<string>(
        name: "--sessionId",
        description: "Session id used to load state",
        getDefaultValue: () => Config.GetConfigOptionDefaultValue(".llmagents-session", "LLMAGENTS_SESSION"));

    public readonly static Option<string> ToolsConfig = new Option<string>(
        name: "--toolsConfig",
        description: "Path to a JSON file with configuration for tool values",
        getDefaultValue: () => Config.GetConfigOptionDefaultValue("tools.json", "LLMAGENTS_TOOLS_CONFIG"));

    public readonly static Option<string> ToolServerAddress = new Option<string>(
        name: "--toolServerAddress",
        description: "The IP address of the tool server",
        getDefaultValue: () => "");

    public readonly static Option<int> ToolServerPort = new Option<int>(
        name: "--toolServerPort",
        description: "The port of the tool server",
        getDefaultValue: () => 5000);

}
