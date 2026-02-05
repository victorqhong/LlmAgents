using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.CommandLineParser;
using LlmAgents.Communication;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Invocation;

using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;
using Parser = LlmAgents.CommandLineParser.Parser;

namespace ConsoleAgent.Commands;

internal class DefaultCommand : RootCommand
{
    private readonly ILoggerFactory loggerFactory;

    public DefaultCommand(ILoggerFactory loggerFactory)
        : base("ConsoleAgent - runs an LLM agent in the console")
    {
        this.loggerFactory = loggerFactory;

        this.SetHandler(CommandHandler);
        AddOption(LlmAgentsOptions.AgentId);
        AddOption(LlmAgentsOptions.ApiEndpoint);
        AddOption(LlmAgentsOptions.ApiKey);
        AddOption(LlmAgentsOptions.ApiModel);
        AddOption(LlmAgentsOptions.ContextSize);
        AddOption(LlmAgentsOptions.MaxCompletionTokens);
        AddOption(LlmAgentsOptions.ApiConfig);
        AddOption(LlmAgentsOptions.Persistent);
        AddOption(LlmAgentsOptions.SystemPromptFile);
        AddOption(LlmAgentsOptions.WorkingDirectory);
        AddOption(LlmAgentsOptions.StorageDirectory);
        AddOption(LlmAgentsOptions.SessionId);
        AddOption(LlmAgentsOptions.StreamOutput);
        AddOption(LlmAgentsOptions.ToolsConfig);
        AddOption(LlmAgentsOptions.ToolServerAddress);
        AddOption(LlmAgentsOptions.ToolServerPort);
    }

    private async Task CommandHandler(InvocationContext context)
    {
        var logger = loggerFactory.CreateLogger(nameof(ConsoleAgent));

        var apiParameters = Parser.ParseApiParameters(context) ?? Config.InteractiveApiConfigSetup();
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
        if (string.IsNullOrEmpty(toolParameters.ToolsConfig) || !File.Exists(toolParameters.ToolsConfig))
        {
            toolParameters.ToolsConfig = Config.InteractiveToolsConfigSetup();
        }

        var sessionParameters = Parser.ParseSessionParameters(context);

        string? systemPrompt = Prompts.DefaultSystemPrompt;
        if (!string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            systemPrompt = File.ReadAllText(sessionParameters.SystemPromptFile);
        }

        var consoleCommunication = new ConsoleCommunication();

        var agent = await LlmAgentFactory.CreateAgent(loggerFactory, consoleCommunication, apiParameters, agentParameters, toolParameters, sessionParameters);
        agent.PreWaitForContent = async () =>
        {
            await consoleCommunication.SendMessage("> ", false);
        };
        agent.PostParseUsage += async (usage) =>
        {
            await consoleCommunication.SendMessage(string.Format("PromptTokens: {0}, CompletionTokens: {1}, TotalTokens: {2}, Context Used: {3}", usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, ((double)usage.TotalTokens / agent.llmApi.ContextSize).ToString("P"), true));
        };

        var cancellationToken = context.GetCancellationToken();

        await agent.Run(cancellationToken);
    }
}
