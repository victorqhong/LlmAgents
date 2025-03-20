namespace LlmAgents.Communication;

public class ConsoleCommunication : IAgentCommunication
{
    public string? WaitForMessage(CancellationToken cancellationToken = default)
    {
        return Console.ReadLine();
    }

    public void SendMessage(string message)
    {
        Console.WriteLine(message);
    }
}
