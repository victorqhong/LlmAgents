using LlmAgents.State;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ConsoleAgent.Commands;

internal class SessionsNewCommand : Command
{
    private readonly ILoggerFactory loggerFactory;

    private readonly ILogger Log;

    public SessionsNewCommand(ILoggerFactory loggerFactory)
        : base("new", "Create a new session")
    {
        this.loggerFactory = loggerFactory;
        Log = loggerFactory.CreateLogger(nameof(SessionsNewCommand));

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
                Log.LogCritical("Could not load config");
                return;
            }
            else if (!config.ContainsKey("agent"))
            {
                Log.LogCritical("Config does not contain 'agent' property");
                return;
            }
            else
            {
                agentId = config.Value<string>("agent");
            }
        }

        var storageDirectory = context.ParseResult.GetValueForOption(ConsoleAgent.Options.StorageDirectory);
        if (string.IsNullOrEmpty(storageDirectory))
        {
            return;
        }

        if (!Path.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }

        Console.WriteLine($"Agent: {agentId}");

        using var stateDatabase = new StateDatabase(loggerFactory, Path.Combine(storageDirectory, $"{agentId}.db"));
        var session = Session.New();
        stateDatabase.CreateSession(session);
        Console.WriteLine(session.SessionId);
    }
}

