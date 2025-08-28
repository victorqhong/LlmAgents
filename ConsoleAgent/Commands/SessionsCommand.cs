using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ConsoleAgent.Commands;

internal class SessionsCommand : Command
{
    private readonly ILoggerFactory loggerFactory;

    private readonly ILogger Log;

    public SessionsCommand(ILoggerFactory loggerFactory)
        : base("sessions", "Lists the sessions of an LLM agent")
    {
        this.loggerFactory = loggerFactory;
        Log = loggerFactory.CreateLogger(nameof(SessionsCommand));

        AddOption(ConsoleAgent.Options.StorageDirectory);
        AddOption(ConsoleAgent.Options.AgentId);
        this.SetHandler(CommandHandler);
    }

    private void CommandHandler(InvocationContext context)
    {
        var agentId = context.ParseResult.GetValueForOption(ConsoleAgent.Options.AgentId);
        if (string.IsNullOrEmpty(agentId))
        {
            var config = Config.GetConfig();
            if (config == null)
            {
                Log.LogError("Could not get agent from config");
                return;
            }
            else if (!config.ContainsKey("agent"))
            {
                Log.LogError("Config does not contain 'agent' property");
                return;
            }
            else
            {
                agentId = config.Value<string>("agent");
            }
        }

        var storageDirectory = context.ParseResult.GetValueForOption(ConsoleAgent.Options.StorageDirectory);
        if (storageDirectory == null)
        {
            return;
        }

        if (!Path.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }

        Console.WriteLine($"Agent: {agentId}");

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
                Console.WriteLine($"Id: {session.SessionId}, Start time: {session.StartTime}, Last active time: {session.LastActive}");
            }
        }
    }
}
