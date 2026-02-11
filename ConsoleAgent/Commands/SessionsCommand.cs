using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;

namespace ConsoleAgent.Commands;

internal class SessionsCommand : Command
{
    private readonly Argument<string> sessionsAgentIdArgument = new("agentId")
    {
        Description = "The agent identifier"
    };

    private readonly ILoggerFactory loggerFactory;

    public SessionsCommand(ILoggerFactory loggerFactory)
        : base("sessions", "Lists the sessions of an LLM agent")
    {
        this.loggerFactory = loggerFactory;

        SetAction(CommandHandler);
        Arguments.Add(sessionsAgentIdArgument);
        Options.Add(LlmAgentsOptions.StorageDirectory);
    }

    private void CommandHandler(ParseResult parseResult)
    {
        var agentId = parseResult.GetValue(sessionsAgentIdArgument);
        var storageDirectory = parseResult.GetValue(LlmAgentsOptions.StorageDirectory);

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
