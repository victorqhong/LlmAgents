using LlmAgents.LlmApi.Content;

namespace LlmAgents.Communication;

public class ConsoleCommunication : IAgentCommunication
{
    public bool NullOutput { get; set; } = false;

    public Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        var line = Console.ReadLine();
        if (line == null)
        {
            return Task.FromResult<IEnumerable<IMessageContent>?>(null);
        }

        return Task.FromResult<IEnumerable<IMessageContent>?>(new[] { new MessageContentText { Text = line } });
    }

    public Task SendMessage(string message, bool newLine = true)
    {
        if (NullOutput)
        {
            return Task.CompletedTask;
        }

        if (newLine)
        {
            Console.WriteLine(message);
        }
        else
        {
            Console.Write(message);
        }

        return Task.CompletedTask;
    }
}
