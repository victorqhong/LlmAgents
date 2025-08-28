using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace ConsoleAgent.Commands;

internal class AgentsSetDefault : Command
{
    private readonly Argument<string> agentIdArgument = new("agentId", "The agent identifier");

    private readonly ILogger Log;

    public AgentsSetDefault(ILoggerFactory loggerFactory)
        : base("set-default", "Sets the default agent for future commands")
    {
        Log = loggerFactory.CreateLogger(nameof(AgentsSetDefault));

        this.SetHandler(CommandHandler);
        AddArgument(agentIdArgument);
        AddOption(ConsoleAgent.Options.StorageDirectory);
    }

    private void CommandHandler(InvocationContext context)
    {
        var agentId = context.ParseResult.GetValueForArgument(agentIdArgument);
        if (string.IsNullOrEmpty(agentId))
        {
            Log.LogError("agentId is null or empty");
            return;
        }

        try
        {
            var configPath = Config.GetProfileConfig("config.json");
            var config = Config.GetConfig();
            if (config == null)
            {
                config = new JObject
                {
                    { "agent", agentId }
                };
                File.WriteAllText(configPath, config.ToString());
            }
            else
            {
                if (config.ContainsKey("agent"))
                {
                    config["agent"] = agentId;
                }
                else
                {
                    config.Add("agent", agentId);
                }

                File.WriteAllText(configPath, config.ToString());
            }

            Console.WriteLine($"Default agent: {agentId}");
        }
        catch (Exception e)
        {
            Log.LogCritical(e, "Exception while setting default agent");
        }
    }
}

