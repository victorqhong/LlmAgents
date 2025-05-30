using LlmAgents.Agents;

namespace LlmAgents.Communication;

public interface IAgentCommunication
{
    Task<IEnumerable<IMessageContent>> WaitForContent(CancellationToken cancellationToken = default);
    Task SendMessage(string message);
}
