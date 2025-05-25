namespace LlmAgents.Communication;

public class ConsoleCommunication : IAgentCommunication
{
    public async Task<string?> WaitForMessage(CancellationToken cancellationToken = default)
    {
        return Console.ReadLine();
    }

    public async Task SendMessage(string message)
    {
        Console.WriteLine(message);
    }
}
