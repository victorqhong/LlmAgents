using LlmAgents.Agents;
using LlmAgents.CommandLineParser;
using LlmAgents.LlmApi;
using LlmAgents.State;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;

using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;
using XmppOptions = XmppAgent.Options;

namespace XmppAgent.Commands;

internal class DefaultCommand : RootCommand
{
    private readonly ILoggerFactory loggerFactory;

    public DefaultCommand(ILoggerFactory loggerFactory)
        : base("XmppAgent")
    {
        this.loggerFactory = loggerFactory;

        this.SetHandler(CommandHandler);
        AddOption(XmppOptions.AgentsConfig);
        AddOption(LlmAgentsOptions.ToolServerAddress);
        AddOption(LlmAgentsOptions.ToolServerPort);
    }

    private async Task CommandHandler(InvocationContext context)
    {
        var logger = loggerFactory.CreateLogger(nameof(XmppAgent));

        var agentsConfigValue = context.ParseResult.GetValueForOption(XmppOptions.AgentsConfig);
        if (string.IsNullOrEmpty(agentsConfigValue) || !File.Exists(agentsConfigValue))
        {
            Console.WriteLine("agentsConfig is invalid or does not exist");
            return;
        }

        var toolParameters = Parser.ParseToolParameters(context);

        var agentTasks = new List<Task>();

        var agentsConfig = JObject.Parse(File.ReadAllText(agentsConfigValue));
        foreach (var agentProperty in agentsConfig.Properties())
        {
            var agentId = agentProperty.Name;
            var apiConfigPath = agentProperty.Value.Value<string>("apiConfig");
            var xmppConfigPath = agentProperty.Value.Value<string>("xmppConfig");
            var toolsConfig = agentProperty.Value.Value<string>("toolsConfig");
            var workingDirectory = agentProperty.Value.Value<string>("workingDirectory");
            var agentDirectory = agentProperty.Value.Value<string>("agentDirectory");

            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(apiConfigPath) || string.IsNullOrEmpty(xmppConfigPath) || string.IsNullOrEmpty(toolsConfig) || string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(agentDirectory))
            {
                logger.LogWarning("{agentId} not configured correctly", agentId);
                continue;
            }

            var systemPromptFile = agentProperty.Value.Value<string>("systemPromptFile") ?? null;
            var persistent = agentProperty.Value.Value<bool>("persistent");

            var apiConfig = JObject.Parse(File.ReadAllText(apiConfigPath));
            var xmppConfig = JObject.Parse(File.ReadAllText(xmppConfigPath));

            var apiEndpoint = apiConfig.Value<string>("apiEndpoint");
            var apiKey = apiConfig.Value<string>("apiKey");
            var apiModel = apiConfig.Value<string>("apiModel");
            var contextSize = apiConfig.Value<int>("contextSize");
            var maxCompletionTokens = apiConfig.Value<int>("maxCompletionTokens");
            var xmppDomain = xmppConfig.Value<string>("xmppDomain");
            var xmppUsername = xmppConfig.Value<string>("xmppUsername");
            var xmppPassword = xmppConfig.Value<string>("xmppPassword");
            var xmppTargetJid = xmppConfig.Value<string>("xmppTargetJid");
            var xmppTrustHost = xmppConfig.Value<bool>("xmppTrustHost");

            if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
            {
                logger.LogWarning("{agentId} apiParameters not configured correctly", agentId);
                continue;
            }

            if (string.IsNullOrEmpty(xmppDomain) || string.IsNullOrEmpty(xmppUsername) || string.IsNullOrEmpty(xmppPassword) || string.IsNullOrEmpty(xmppTargetJid))
            {
                logger.LogWarning("{agentId} xmppParameters not configured correctly", agentId);
                continue;
            }

            var apiParameters = new LlmApiOpenAiParameters
            {
                ApiEndpoint = apiEndpoint,
                ApiKey = apiKey,
                ApiModel = apiModel,
                ContextSize = contextSize,
                MaxCompletionTokens = maxCompletionTokens
            };

            var agentParameters = new LlmAgentParameters
            {
                AgentId = agentId,
                Persistent = persistent,
                StorageDirectory = agentDirectory,
                StreamOutput = false
            };

            var sessionParameters = new SessionParameters
            {
                SystemPromptFile = systemPromptFile,
                WorkingDirectory = workingDirectory,
            };

            var xmppParameters = new XmppParameters
            {
                XmppDomain = xmppDomain,
                XmppUsername = xmppUsername,
                XmppPassword = xmppPassword,
                XmppTrustHost = xmppTrustHost,
                XmppTargetJid = xmppTargetJid,
            };

            agentTasks.Add(AgentFactory.RunAgent(apiParameters, agentParameters, toolParameters, sessionParameters, xmppParameters, context.GetCancellationToken()));
        }

        if (agentTasks.Count < 1)
        {
            logger.LogError("There were no agentTasks");
            return;
        }

        await Task.WhenAll(agentTasks);
    }
}
