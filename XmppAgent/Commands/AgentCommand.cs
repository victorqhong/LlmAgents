using LlmAgents;
using LlmAgents.CommandLineParser;
using Microsoft.Extensions.Logging;
using System.CommandLine;

using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;
using XmppOptions = XmppAgent.Options;

namespace XmppAgent.Commands;

internal class AgentCommand : Command
{
    private readonly ILoggerFactory loggerFactory;

    public AgentCommand(ILoggerFactory loggerFactory)
        : base("agent", "Run a single XMPP agent")
    {
        this.loggerFactory = loggerFactory;

        SetAction(CommandHandler);
        Options.Add(LlmAgentsOptions.ApiEndpoint);
        Options.Add(LlmAgentsOptions.ApiKey);
        Options.Add(LlmAgentsOptions.ApiModel);
        Options.Add(LlmAgentsOptions.ContextSize);
        Options.Add(LlmAgentsOptions.MaxCompletionTokens);
        Options.Add(LlmAgentsOptions.ApiConfig);
        Options.Add(LlmAgentsOptions.Persistent);
        Options.Add(LlmAgentsOptions.SystemPromptFile);
        Options.Add(LlmAgentsOptions.ToolsConfig);
        Options.Add(LlmAgentsOptions.McpConfigPath);
        Options.Add(LlmAgentsOptions.WorkingDirectory);
        Options.Add(LlmAgentsOptions.StorageDirectory);
        Options.Add(LlmAgentsOptions.AgentId);

        Options.Add(XmppOptions.XmppDomain);
        Options.Add(XmppOptions.XmppUsername);
        Options.Add(XmppOptions.XmppPassword);
        Options.Add(XmppOptions.XmppTargetJid);
        Options.Add(XmppOptions.XmppTrustHost);
        Options.Add(XmppOptions.XmppConfig);
    }

    private async Task CommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(XmppAgent));

        var apiParameters = Parser.ParseApiParameters(parseResult);
        if (apiParameters == null)
        {
            Console.Error.WriteLine("apiEndpoint, apiKey, and/or apiModel is null or empty.");
            return;
        }

        if (apiParameters.ContextSize < 1)
        {
            logger.LogWarning("Context size must be greater than zero. Setting to default 8192");
            apiParameters.ContextSize = 8192;
        }

        if (apiParameters.MaxCompletionTokens < 1)
        {
            logger.LogWarning("Maximum completion tokens must be greater than zero. Setting to default 8192");
            apiParameters.MaxCompletionTokens = 8192;
        }

        var agentParameters = Parser.ParseAgentParameters(parseResult);
        if (agentParameters == null)
        {
            logger.LogError("agentParameters not configured correctly");
            return;
        }

        var toolParameters = Parser.ParseToolParameters(parseResult);
        var sessionParameters = Parser.ParseSessionParameters(parseResult);

        string? systemPrompt = Prompts.DefaultSystemPrompt;
        if (!string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            systemPrompt = File.ReadAllText(sessionParameters.SystemPromptFile);
        }

        var xmppParameters = XmppParameters.ParseXmppParameters(parseResult);
        if (xmppParameters == null)
        {
            logger.LogError("xmppParameters not configured correctly");
            return;
        }

        await AgentFactory.RunAgent(apiParameters, agentParameters, toolParameters, sessionParameters, xmppParameters, cancellationToken);
    }
}
