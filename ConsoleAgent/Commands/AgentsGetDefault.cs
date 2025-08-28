using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ConsoleAgent.Commands;

internal class AgentsGetDefault : Command
{
    private readonly Argument<string> agentIdArgument = new("agentId", "The agent identifier");

    private readonly ILogger Log;

    public AgentsGetDefault(ILoggerFactory loggerFactory)
        : base("get-default", "Get the default agent for future commands")
    {
        Log = loggerFactory.CreateLogger(nameof(AgentsSetDefault));

        AddOption(ConsoleAgent.Options.StorageDirectory);
        this.SetHandler(CommandHandler);
    }

    private void CommandHandler(InvocationContext context)
    {
        var config = Config.GetConfig();
        if (config == null)
        {
            Log.LogWarning("Could not find config");
        }
        else if (!config.ContainsKey("agent"))
        {
            Log.LogWarning("Config does not contain 'agent' property");
        }
        else
        {
            Console.WriteLine(config.Value<string>("agent"));
        }
    }
}

