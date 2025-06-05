using LlmAgents.LlmApi.Content;

namespace LlmAgents.Communication;

public class ConsoleCommunication : IAgentCommunication
{
    public async Task<IEnumerable<IMessageContent>> WaitForContent(CancellationToken cancellationToken = default)
    {
        return new[] { new MessageContentText { Text = Console.ReadLine() ?? string.Empty } };
    }

    public async Task SendMessage(string message)
    {
        Console.WriteLine(message);
    }
}
