using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ConsoleAgent.Commands;

internal class AgentsCommand : Command
{
    private readonly ILogger Log;

    public AgentsCommand(ILoggerFactory loggerFactory)
        : base("agents", "Lists persistent agents")
    {
        Log = loggerFactory.CreateLogger(nameof(AgentsCommand));

        AddOption(ConsoleAgent.Options.StorageDirectory);
        this.SetHandler(CommandHandler);
    }

    private void CommandHandler(InvocationContext context)
    {
        var storageDirectory = context.ParseResult.GetValueForOption(ConsoleAgent.Options.StorageDirectory);
        if (string.IsNullOrEmpty(storageDirectory) || !Path.Exists(storageDirectory))
        {
            return;
        }

        var files = Directory.GetFiles(storageDirectory, "*.db", SearchOption.TopDirectoryOnly);
        var agents = files.Select(Path.GetFileNameWithoutExtension);
        foreach (var agent in agents)
        {
            Console.WriteLine(agent);
        }
    }
}
