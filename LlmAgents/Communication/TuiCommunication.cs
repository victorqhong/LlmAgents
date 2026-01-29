using System.Text;
using System.Threading.Channels;
using LlmAgents.LlmApi.Content;

namespace LlmAgents.Communication;

public class TuiCommunication : IAgentCommunication
{
    private volatile bool _exitRequested = false;
    private readonly Lock _consoleLock = new();
    private readonly Channel<string> channel = Channel.CreateBounded<string>(1);

    private StringBuilder sb = new();

    private readonly List<string> logLines = [];
    private int outputHeight = 30;         // Lines for output/log
    private int outputWidth;
    private int windowHeight = 30 + 3;     // + prompt + input line + padding
    private int windowWidth;

    private bool linesAdded = false;

    public TuiCommunication()
    {
        windowHeight = Console.WindowHeight - 2;
        windowWidth = Console.WindowWidth;
        outputHeight = windowHeight - 3;
        outputWidth = windowWidth;

        Console.CursorVisible = false;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Clear();

        var renderThread = new Thread(RenderLoop) { IsBackground = true };
        renderThread.Start();

        var inputThread = new Thread(InputLoop) { IsBackground = true };
        inputThread.Start();
    }

    public async Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        var input = await channel.Reader.ReadAsync(cancellationToken);
        return new[] { new MessageContentText { Text = input } };
    }

    public async Task SendMessage(string message, bool newLine = true)
    {
        await AddToLog(message, newLine);
        linesAdded = true;
    }

    private async Task AddToLog(string message, bool newLine)
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

            while (logLines[^1].Length > outputWidth)
            {
                var split1 = logLines[^1].Substring(0, outputWidth);
                var split2 = logLines[^1].Substring(outputWidth);

                logLines[^1] = split1;
                logLines.Add(split2);
            }
        }

        while (logLines.Count > outputHeight)
        {
            logLines.RemoveAt(0);
        }
    }

    private void RenderLoop()
    {
        while (!_exitRequested)
        {
            Thread.Sleep(33);
            if (!linesAdded)
            {
                continue;
            }

            lock (_consoleLock)
            {
                var outputBuffer = new List<string>(outputHeight);
                int startIndex = Math.Max(0, logLines.Count - outputHeight);
                int count = Math.Min(logLines.Count, outputHeight);
                for (int i = 0; i < count; i++)
                {
                    var line = (logLines[startIndex + i] ?? string.Empty).PadRight(outputWidth);
                    outputBuffer.Add(line);
                }

                var input = sb.ToString();

                var origLeft = Console.CursorLeft;
                var origTop = Console.CursorTop;
                var origColor = Console.ForegroundColor;

                Console.SetCursorPosition(0, 0);
                Console.Write(input.PadRight(Console.WindowWidth -1));

                Console.SetCursorPosition(0, 2);
                Console.Write(string.Join(Environment.NewLine, outputBuffer));

                Console.SetCursorPosition(origLeft, origTop);
                Console.ForegroundColor = origColor;

                linesAdded = false;
            }
        }
    }

    private async void InputLoop()
    {
        while (!_exitRequested)
        {
            Thread.Sleep(33);
            int c = -1;
            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo keyInfo;
                keyInfo = Console.ReadKey(true);

                c = keyInfo.KeyChar;
                if (keyInfo.Key == ConsoleKey.Backspace && sb.Length > 0)
                {
                    sb = sb.Remove(sb.Length - 1, 1);
                }
                else if (keyInfo.Key == ConsoleKey.Enter)
                {
                    sb.Append(Environment.NewLine);
                }
                else
                {
                    sb.Append(keyInfo.KeyChar);
                }

                linesAdded = true;
            }

            if (c == -1)
            {
                continue;
            }

            if (c != 13)
            {
                continue;
            }

            var line = sb.ToString();
            sb.Clear();

            lock (_consoleLock)
            {
                var origLeft = Console.CursorLeft;
                var origTop = Console.CursorTop;
                var origColor = Console.ForegroundColor;

                Console.SetCursorPosition(0, 2);
                for (int i = 0; i < Console.BufferHeight - 2; i++)
                {
                    Console.WriteLine(" ".PadRight(Console.BufferWidth));
                }

                Console.SetCursorPosition(origLeft, origTop);
                Console.ForegroundColor = origColor;
            }

            await channel.Writer.WriteAsync(line);
        }
    }

}
