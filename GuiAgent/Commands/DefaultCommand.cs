using GuiAgent.Tools;
using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.CommandLineParser;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using LlmAgentsOptions = LlmAgents.CommandLineParser.Options;
using Parser = LlmAgents.CommandLineParser.Parser;

namespace GuiAgent.Commands;

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
        Options.Add (LlmAgentsOptions.ApiModel);
        Options.Add(LlmAgentsOptions.ContextSize);
        Options.Add(LlmAgentsOptions.MaxCompletionTokens);
        Options.Add(LlmAgentsOptions.ApiConfig);
        Options.Add(LlmAgentsOptions.Persistent);
        Options.Add(LlmAgentsOptions.SystemPromptFile);
        Options.Add(LlmAgentsOptions.WorkingDirectory);
        Options.Add(LlmAgentsOptions.StorageDirectory);
        Options.Add(LlmAgentsOptions.SessionId);
        Options.Add(LlmAgentsOptions.StreamOutput);
        Options.Add(LlmAgentsOptions.ToolsConfig);
        Options.Add(LlmAgentsOptions.McpConfigPath);
    }

    private async Task CommandHandler(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(GuiAgent));

        var apiParameters = Parser.ParseApiParameters(parseResult) ?? Config.InteractiveApiConfigSetup();
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
        if (string.IsNullOrEmpty(toolParameters.ToolsConfig) || !File.Exists(toolParameters.ToolsConfig))
        {
            toolParameters.ToolsConfig = Config.InteractiveToolsConfigSetup();
        }

        var sessionParameters = Parser.ParseSessionParameters(parseResult);

        string? systemPrompt = Prompts.DefaultSystemPrompt;
        if (!string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            systemPrompt = File.ReadAllText(sessionParameters.SystemPromptFile);
        }

        var consoleCommunication = new ConsoleCommunication();

        var api = new LlmApiOpenAi(loggerFactory, apiParameters);
        var agent = new GuiAgent.Agents.GuiAgent(agentParameters, api, consoleCommunication);
        await LlmAgentFactory.ConfigureAgent(agent, loggerFactory, consoleCommunication, agentParameters, toolParameters, sessionParameters);

        agent.PreWaitForContent = () => { consoleCommunication.SendMessage("> ", false); };
        agent.PostParseUsage += (usage) => { consoleCommunication.SendMessage(string.Format("\nPromptTokens: {0}, CompletionTokens: {1}, TotalTokens: {2}, Context Used: {3}", usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, ((double)usage.TotalTokens / agent.llmApi.ContextSize).ToString("P"))); };

        var toolFactory = new ToolFactory(loggerFactory);
        agent.AddTool(new KeyboardType(toolFactory));
        agent.AddTool(new MouseClick(toolFactory));

        await agent.Run(cancellationToken);
    }
}
