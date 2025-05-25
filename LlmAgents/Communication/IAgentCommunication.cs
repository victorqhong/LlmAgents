namespace LlmAgents.Communication;

public interface IAgentCommunication
{
    Task<string?> WaitForMessage(CancellationToken cancellationToken = default);
    Task SendMessage(string message);
}
