using System.CommandLine;

namespace LlmAgents.CommandLineParser;

public static class Options
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

    public readonly static Option<int> ContextSize = new Option<int>(
        name: "--contextSize",
        description: "Maximum number of tokens to use with the api",
        getDefaultValue: () => 8192);

    public readonly static Option<int> MaxCompletionTokens = new Option<int>(
        name: "--maxCompletionTokens",
        description: "Maximum number of tokens in a completion",
        getDefaultValue: () => 8192);

    public readonly static Option<string?> ApiConfig = new Option<string?>(
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

    public readonly static Option<string?> SessionId = new Option<string?>(
        name: "--sessionId",
        description: "Session id used to load state",
        getDefaultValue: () => Config.GetConfigOptionDefaultValue(".llmagents-session", "LLMAGENTS_SESSION"));

    public readonly static Option<string?> ToolsConfig = new Option<string?>(
        name: "--toolsConfig",
        description: "Path to a JSON file with configuration for tool values",
        getDefaultValue: () => Config.GetConfigOptionDefaultValue("tools.json", "LLMAGENTS_TOOLS_CONFIG"));

    public readonly static Option<string?> McpConfigPath = new Option<string?>(
        name: "--mcpConfigPath",
        description: "Path to the MCP config JSON",
        getDefaultValue: () => Config.GetConfigOptionDefaultValue("mcp.json", "LLMAGENTS_MCP_CONFIG"));

    public readonly static Option<bool> StreamOutput = new Option<bool>(
        name: "--streamOutput",
        description: "Whether to stream the output responses",
        getDefaultValue: () => true);
}
