using LlmAgents.LlmApi.Content;
using System.Text;

namespace LlmAgents.Communication;

public class ConsoleCommunication : IAgentCommunication
{
    public async Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        var line = await ReadConsoleLineAsync(cancellationToken);
        if (line == null)
        {
            return null;
        }

        return new [] { new MessageContentText { Text = line } };
    }

    public Task SendMessage(string message, bool newLine = true)
    {
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

    private static async Task<string?> ReadConsoleLineAsync(CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
        {
            return await Console.In.ReadLineAsync(cancellationToken);
        }

        var input = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested)
        {
            while (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(intercept: true);
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return input.ToString();
                }

                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (input.Length == 0)
                    {
                        continue;
                    }

                    input.Length--;
                    Console.Write("\b \b");
                    continue;
                }

                if (char.IsControl(keyInfo.KeyChar))
                {
                    continue;
                }

                input.Append(keyInfo.KeyChar);
                Console.Write(keyInfo.KeyChar);
            }

            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }
}

