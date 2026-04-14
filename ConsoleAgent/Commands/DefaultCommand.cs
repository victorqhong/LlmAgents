using LlmAgents.Agents;
using LlmAgents.Agents.Work;
using LlmAgents.Api.Extensions;
using LlmAgents.Communication;
using Microsoft.Extensions.Logging;
using System.CommandLine;
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

        SetAction(CommandHandler);
        Options.Add(LlmAgentsOptions.AgentId);
        Options.Add(LlmAgentsOptions.ApiEndpoint);
        Options.Add(LlmAgentsOptions.ApiKey);
        Options.Add(LlmAgentsOptions.ApiModel);
        Options.Add(LlmAgentsOptions.ContextSize);
        Options.Add(LlmAgentsOptions.ApiConfig);
        Options.Add(LlmAgentsOptions.Persistent);
        Options.Add(LlmAgentsOptions.SystemPromptFile);
        Options.Add(LlmAgentsOptions.WorkingDirectory);
        Options.Add(LlmAgentsOptions.StorageDirectory);
        Options.Add(LlmAgentsOptions.Session);
        Options.Add(LlmAgentsOptions.StreamOutput);
        Options.Add(LlmAgentsOptions.ToolsConfig);
        Options.Add(LlmAgentsOptions.McpConfigPath);
        Options.Add(LlmAgentsOptions.AgentManagerUrl);
        Options.Add(LlmAgentsOptions.Debug);
    }

    private async Task CommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(ConsoleAgent));

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

        var agentParameters = Parser.ParseAgentParameters(parseResult);
        if (agentParameters == null)
        {
            logger.LogError("agentParameters not configured correctly");
            return;
        }

        var toolParameters = Parser.ParseToolParameters(parseResult);
        var sessionParameters = Parser.ParseSessionParameters(parseResult);
        sessionParameters.OutputMessagesOnLoad = true;

        var consoleCommunication = new ConsoleCommunication();

        var agent = await LlmAgentFactory.CreateAgent(loggerFactory, consoleCommunication, apiParameters, agentParameters, toolParameters, sessionParameters);
        agent.PreWaitForContent += async () =>
        {
            await consoleCommunication.SendMessage("User: ", false);
        };

        agent.PostParseUsage += async (usage) =>
        {
            await consoleCommunication.SendMessage(string.Format("PromptTokens: {0}, CompletionTokens: {1}, TotalTokens: {2}, Context Used: {3}", usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, ((double)usage.TotalTokens / agent.llmApi.ApiConfig.ContextSize).ToString("P"), true));
        };

        if (parseResult.GetValue(LlmAgentsOptions.Debug))
        {
            agent.CreateAssistantResponseWork = agent =>
            {
                return new GetAssistantResponseWork(loggerFactory, agent)
                {
                    OutputReasoning = true,
                };
            };
        }

        if (agentParameters.AgentManagerUrl != null)
        {
            await agent.ConfigureAgentHub(agentParameters.AgentManagerUrl, consoleCommunication, logger);
        }

        await agent.Run(cancellationToken);
    }
}
