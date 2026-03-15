using LlmAgents.Agents;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Text.Json;
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

        SetAction(CommandHandler);
        Options.Add(XmppOptions.AgentsConfig);
        Options.Add(LlmAgentsOptions.McpConfigPath);
    }

    private async Task CommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(XmppAgent));

        var agentsConfigValue = parseResult.GetValue(XmppOptions.AgentsConfig);
        if (string.IsNullOrEmpty(agentsConfigValue) || !File.Exists(agentsConfigValue))
        {
            logger.LogError("agentsConfig is invalid or does not exist");
            return;
        }

        var agentTasks = new List<Task>();

        var agentsConfig = JsonSerializer.Deserialize<Dictionary<string, AgentConfig>>(File.ReadAllText(agentsConfigValue));
        if (agentsConfig == null)
        {
            return;
        }

        foreach (var agentProperty in agentsConfig)
        {
            var agentId = agentProperty.Key;
            var apiConfigPath = agentProperty.Value.ApiConfig;
            var xmppConfigPath = agentProperty.Value.XmppConfig;
            var toolsConfig = agentProperty.Value.ToolsConfig;
            var workingDirectory = agentProperty.Value.WorkingDirectory;
            var agentDirectory = agentProperty.Value.AgentDirectory;

            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(apiConfigPath) || string.IsNullOrEmpty(xmppConfigPath) || string.IsNullOrEmpty(toolsConfig) || string.IsNullOrEmpty(workingDirectory) || string.IsNullOrEmpty(agentDirectory))
            {
                logger.LogWarning("{agentId} not configured correctly", agentId);
                continue;
            }

            var systemPromptFile = agentProperty.Value.SystemPromptFile;
            var persistent = agentProperty.Value.Persistent;

            var apiParameters = JsonSerializer.Deserialize<LlmApiOpenAiParameters>(File.ReadAllText(apiConfigPath));
            var xmppParameters = JsonSerializer.Deserialize<XmppConfig>(File.ReadAllText(xmppConfigPath));

            if (apiParameters == null || !apiParameters.Valid())
            {
                logger.LogWarning("{agentId} apiParameters not configured correctly", agentId);
                continue;
            }

            if (xmppParameters == null || !xmppParameters.Valid())
            {
                logger.LogWarning("{agentId} xmppParameters not configured correctly", agentId);
                continue;
            }

            var agentParameters = new LlmAgentParameters
            {
                AgentId = agentId,
                Persistent = persistent,
                StorageDirectory = agentDirectory,
                StreamOutput = false,
                AgentManagerUrl = null
            };

            var sessionParameters = new SessionParameters
            {
                SystemPromptFile = systemPromptFile,
                WorkingDirectory = workingDirectory,
            };

            var toolParameters = new ToolParameters
            {
                ToolsConfig = toolsConfig,
            };

            agentTasks.Add(AgentFactory.RunAgent(apiParameters, agentParameters, toolParameters, sessionParameters, xmppParameters, cancellationToken));
        }

        if (agentTasks.Count < 1)
        {
            logger.LogError("There were no agentTasks");
            return;
        }

        await Task.WhenAll(agentTasks);
    }
}
