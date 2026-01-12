using LlmAgents.LlmApi.Content;

namespace LlmAgents.Communication;

public class ConsoleCommunication : IAgentCommunication
{
    private int outputHeight = 30;         // Lines for output/log
    private int outputWidth;
    private int windowHeight = 30 + 3;     // + prompt + input line + padding
    private int windowWidth;

    private readonly List<string> logLines = [];

    public ConsoleCommunication()
    {
        windowHeight = Console.WindowHeight - 2;
        windowWidth = Console.WindowWidth;
        outputHeight = windowHeight - 3;
        outputWidth = windowWidth;

        Console.Clear();
        Console.WriteLine("=== OUTPUT LOG ===");
        Console.WriteLine(new string('-', Console.WindowWidth - 1));

        // Leave output area blank initially
        for (int i = 0; i < outputHeight; i++)
            Console.WriteLine();

        Console.WriteLine(new string('-', Console.WindowWidth - 1));

        Console.SetCursorPosition(0, windowHeight);
        Console.Write("Enter message (or 'quit' to exit): ");
    }

    public Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        Console.SetCursorPosition(0, windowHeight);
        Console.Write(new string(' ', Console.WindowWidth - 1)); // Clear old input line
        Console.SetCursorPosition(0, windowHeight);
        Console.Write("Enter message (or 'quit' to exit): ");
        Console.SetCursorPosition(35, windowHeight); // Position after prompt

        var line = Console.ReadLine();
        if (line == null)
        {
            return Task.FromResult<IEnumerable<IMessageContent>?>(null);
        }

        return Task.FromResult<IEnumerable<IMessageContent>?>(new[] { new MessageContentText { Text = line } });
    }

    public Task SendMessage(string message, bool newLine = true)
    {
        AddToLog(message, newLine);

        return Task.CompletedTask;
    }

    void AddToLog(string message, bool newLine)
    {
        var appendToOutput = !newLine;
        var lines = message.Split('\n');

        if (appendToOutput && logLines.Count == 0)
        {
            logLines.Add(string.Empty);
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 0 && appendToOutput)
            {
                logLines[^1] += lines[i];
            }
            else
            {
                logLines.Add(lines[i]);
            }

            while (logLines[^1].Length > Console.WindowWidth)
            {
                var split1 = logLines[^1].Substring(0, Console.WindowWidth);
                var split2 = logLines[^1].Substring(Console.WindowWidth);

                logLines[^1] = split1;
                logLines.Add(split2);
            }
        }

        while (logLines.Count > outputHeight)
        {
            logLines.RemoveAt(0);
        }

        RedrawOutputArea();
    }

    void RedrawOutputArea()
    {
        var savedLeft = Console.CursorLeft;
        var savedTop = Console.CursorTop;

        Console.CursorVisible = false;

        // Move to start of output area (line 2)
        Console.SetCursorPosition(0, 2);

        var outputBuffer = new List<string>(logLines);
        for (int i = 0; i < outputBuffer.Count; i++)
        {
            outputBuffer[i] = logLines[i].PadRight(outputWidth);
        }

        Console.Write(string.Join(Environment.NewLine, outputBuffer));

        Console.SetCursorPosition(savedLeft, savedTop);

        Console.CursorVisible = true;
    }
}
