using LlmAgents.LlmApi.Content;

namespace LlmAgents.Communication;

public class ConsoleCommunication : IAgentCommunication
{
    public async Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        var line = Console.ReadLine();
        if (line == null)
        {
            return null;
        }

        return new[] { new MessageContentText { Text = line } };
    }

    public async Task SendMessage(string message, bool newLine = true)
    {
        if (newLine)
        {
            Console.WriteLine(message);
        }
        else
        {
            Console.Write(message);
        }
    }
}
