using System.CommandLine;

namespace LlmAgents.CommandLineParser;

public static class Options
{
    public readonly static Option<string> AgentId = new("--agentId")
    {
        Description = "Value used to identify the agent",
        DefaultValueFactory = result => Environment.MachineName
    };

    public readonly static Option<string> ApiEndpoint = new("--apiEndpoint")
    {
        Description = "HTTP(s) endpoint of OpenAI compatible API"
    };

    public readonly static Option<string> ApiKey = new("--apiKey")
    {
        Description = "Key used to authenticate to the api",
    };

    public readonly static Option<string> ApiModel = new("--apiModel")
    {
        Description = "Name of the model to include in requests"
    };

    public readonly static Option<int> ContextSize = new("--contextSize")
    {
        Description = "Maximum number of tokens to use with the api",
        DefaultValueFactory = result => 8192,
    };

    public readonly static Option<string?> ApiConfig = new("--apiConfig")
    {
        Description = "Path to a JSON file with configuration for api values",
        DefaultValueFactory = result => Config.GetConfigOptionDefaultValue("api.json", "LLMAGENTS_API_CONFIG")
    };

    public readonly static Option<bool> Persistent = new("--persistent")
    {
        Description = "Include option to save messages and state",
        DefaultValueFactory = result => false
    };

    public readonly static Option<string> SystemPromptFile = new("--systemPromptFile")
    {
        Description = "The path to a file containing the system prompt text. Option has no effect if messages are loaded from a previous persistent session.",
        DefaultValueFactory = result => ""
    };

    public readonly static Option<string> WorkingDirectory = new("--workingDirectory")
    {
        Description = "Directory which agent has access to by default",
        DefaultValueFactory = result => Environment.CurrentDirectory
    };

    public readonly static Option<string> StorageDirectory = new("--storageDirectory")
    {
        Description = "Directory used to store agent related data",
        DefaultValueFactory = result => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlmAgents")
    };

    public readonly static Option<string?> Session = new("--session")
    {
        Description = "Can be \"new\", \"choose\", \"latest\", or a session id used to load and save state",
        DefaultValueFactory = result => Config.GetConfigOptionDefaultValue(".llmagents-session", "LLMAGENTS_SESSION")
    };

    public readonly static Option<string?> ToolsConfig = new("--toolsConfig")
    {
        Description = "Path to a JSON file with configuration for tool values",
        DefaultValueFactory = result => Config.GetConfigOptionDefaultValue("tools.json", "LLMAGENTS_TOOLS_CONFIG")
    };

    public readonly static Option<string?> McpConfigPath = new("--mcpConfigPath")
    {
        Description = "Path to the MCP config JSON",
        DefaultValueFactory = result => Config.GetConfigOptionDefaultValue("mcp.json", "LLMAGENTS_MCP_CONFIG")
    };

    public readonly static Option<bool> StreamOutput = new("--streamOutput")
    {
        Description = "Whether to stream the output responses",
        DefaultValueFactory = result => true
    };

    public readonly static Option<string?> AgentManagerUrl = new ("--managerUrl")
    {
        Description = "URL of the AgentManager server to connect to",
        DefaultValueFactory = result => null
    };

    public readonly static Option<bool> Debug = new("--debug")
    {
        Description = "Whether to enable debug logging",
        DefaultValueFactory = result => false
    };
}
