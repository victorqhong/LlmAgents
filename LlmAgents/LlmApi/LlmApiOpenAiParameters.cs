namespace LlmAgents.LlmApi;

public class LlmApiOpenAiParameters
{
    public required string ApiEndpoint;
    public required string ApiKey;
    public required string ApiModel;
    public required int ContextSize;
    public required int MaxCompletionTokens;
}