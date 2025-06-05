using LlmAgents.LlmApi.Content;

namespace LlmAgents.Communication;

public interface IAgentCommunication
{
    Task<IEnumerable<IMessageContent>> WaitForContent(CancellationToken cancellationToken = default);
    Task SendMessage(string message);
}
