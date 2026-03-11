namespace LlmAgents.LlmApi;

public class LlmApiOpenAiParameters
{
    public required string ApiEndpoint { get; set; }
    public required string ApiKey { get; set; }
    public required string ApiModel { get; set; }
    public required int ContextSize { get; set; }
    public required int MaxCompletionTokens { get; set; }
}
