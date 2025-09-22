namespace RLArena;

internal class AgentParameters
{
    public required string ApiEndpoint;
    public required string ApiKey;
    public required string ApiModel;
    public required int ContextSize;
    public required int MaxCompletionTokens;
    public required string AgentId;
    public required string WorkingDirectory;
    public required string StorageDirectory;
    public required string ToolsFilePath;
}