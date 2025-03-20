namespace LlmAgents.Communication;

public interface IAgentCommunication
{
    string? WaitForMessage(CancellationToken cancellationToken = default);
    void SendMessage(string message);
}
