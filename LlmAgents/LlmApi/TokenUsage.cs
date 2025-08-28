namespace LlmAgents.LlmApi;

public class TokenUsage
{
    public required int PromptTokens;
    public required int CompletionTokens;
    public required int TotalTokens;
}