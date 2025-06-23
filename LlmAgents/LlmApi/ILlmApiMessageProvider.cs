namespace LlmAgents.LlmApi;

public interface ILlmApiMessageProvider
{
    Task<int> CountMessages();

    Task PruneContext(int numMessagesToKeep);
}