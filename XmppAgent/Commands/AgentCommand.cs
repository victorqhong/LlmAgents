using LlmAgents;
using LlmAgents.CommandLineParser;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;

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

        this.SetHandler(CommandHandler);
        AddOption(LlmAgentsOptions.ApiEndpoint);
        AddOption(LlmAgentsOptions.ApiKey);
        AddOption(LlmAgentsOptions.ApiModel);
        AddOption(LlmAgentsOptions.ContextSize);
        AddOption(LlmAgentsOptions.MaxCompletionTokens);
        AddOption(LlmAgentsOptions.ApiConfig);
        AddOption(LlmAgentsOptions.Persistent);
        AddOption(LlmAgentsOptions.SystemPromptFile);
        AddOption(LlmAgentsOptions.ToolsConfig);
        AddOption(LlmAgentsOptions.McpConfigPath);
        AddOption(LlmAgentsOptions.WorkingDirectory);
        AddOption(LlmAgentsOptions.StorageDirectory);
        AddOption(LlmAgentsOptions.AgentId);

        AddOption(XmppOptions.XmppDomain);
        AddOption(XmppOptions.XmppUsername);
        AddOption(XmppOptions.XmppPassword);
        AddOption(XmppOptions.XmppTargetJid);
        AddOption(XmppOptions.XmppTrustHost);
        AddOption(XmppOptions.XmppConfig);
    }

    private async Task CommandHandler(InvocationContext context)
    {
        var logger = loggerFactory.CreateLogger(nameof(XmppAgent));

        var apiParameters = Parser.ParseApiParameters(context);
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

        var agentParameters = Parser.ParseAgentParameters(context);
        if (agentParameters == null)
        {
            logger.LogError("agentParameters not configured correctly");
            return;
        }

        var toolParameters = Parser.ParseToolParameters(context);
        var sessionParameters = Parser.ParseSessionParameters(context);

        string? systemPrompt = Prompts.DefaultSystemPrompt;
        if (!string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            systemPrompt = File.ReadAllText(sessionParameters.SystemPromptFile);
        }

        var xmppParameters = XmppParameters.ParseXmppParameters(context);
        if (xmppParameters == null)
        {
            logger.LogError("xmppParameters not configured correctly");
            return;
        }

        await AgentFactory.RunAgent(apiParameters, agentParameters, toolParameters, sessionParameters, xmppParameters, context.GetCancellationToken());
    }
}
