using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ConsoleAgent.Commands;

internal class SessionsCommand : Command
{
    private readonly Argument<string> sessionsAgentIdArgument = new("agentId", "The agent identifier");

    private readonly ILoggerFactory loggerFactory;

    public SessionsCommand(ILoggerFactory loggerFactory)
        : base("sessions", "Lists the sessions of an LLM agent")
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

        using var stateDatabase = new StateDatabase(loggerFactory, Path.Combine(storageDirectory, $"{agentId}.db"));
        var sessions = stateDatabase.GetSessions();
        if (sessions.Count == 0)
        {
            Console.WriteLine("No sessions");
        }
        else
        {
            foreach (var session in sessions)
            {
                Console.WriteLine($"id: {session.SessionId}");
            }
        }
    }
}