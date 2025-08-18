using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ConsoleAgent.Commands;

internal class NewSessionCommand : Command
{
    private readonly Argument<string> sessionsAgentIdArgument = new("agentId", "The agent identifier");

    private readonly ILoggerFactory loggerFactory;

    public NewSessionCommand(ILoggerFactory loggerFactory)
        : base("new", "Create a new LLM agent session")
    {
        this.loggerFactory = loggerFactory;

        this.SetHandler(CommandHandler);
        AddArgument(sessionsAgentIdArgument);
        AddOption(ConsoleAgent.Options.StorageDirectory);
    }

    private void CommandHandler(InvocationContext context)
    {
        var agentId = context.ParseResult.GetValueForArgument(sessionsAgentIdArgument);
        var storageDirectory = context.ParseResult.GetValueForOption(ConsoleAgent.Options.StorageDirectory);

        if (storageDirectory == null)
        {
            return;
        }

        if (!Path.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }

        var session = Session.New();

        using var stateDatabase = new StateDatabase(loggerFactory, Path.Combine(storageDirectory, $"{agentId}.db"));
        stateDatabase.CreateSession(session);
        Console.WriteLine($"{session.SessionId}");
    }
}

