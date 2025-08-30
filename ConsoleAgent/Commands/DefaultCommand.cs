using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Net.Sockets;

namespace ConsoleAgent.Commands;

internal class DefaultCommand : RootCommand
{
    private readonly ILoggerFactory loggerFactory;

    public DefaultCommand(ILoggerFactory loggerFactory)
        : base("ConsoleAgent - runs an LLM agent in the console")
    {
        this.loggerFactory = loggerFactory;

        this.SetHandler(CommandHandler);
        AddOption(ConsoleAgent.Options.AgentId);
        AddOption(ConsoleAgent.Options.ApiEndpoint);
        AddOption(ConsoleAgent.Options.ApiKey);
        AddOption(ConsoleAgent.Options.ApiModel);
        AddOption(ConsoleAgent.Options.ContextSize);
        AddOption(ConsoleAgent.Options.ApiConfig);
        AddOption(ConsoleAgent.Options.Persistent);
        AddOption(ConsoleAgent.Options.SystemPromptFile);
        AddOption(ConsoleAgent.Options.WorkingDirectory);
        AddOption(ConsoleAgent.Options.StorageDirectory);
        AddOption(ConsoleAgent.Options.SessionId);
        AddOption(ConsoleAgent.Options.ToolsConfig);
        AddOption(ConsoleAgent.Options.ToolServerAddress);
        AddOption(ConsoleAgent.Options.ToolServerPort);
    }

    private async Task CommandHandler(InvocationContext context)
    {
        var apiEndpoint = string.Empty;
        var apiKey = string.Empty;
        var apiModel = string.Empty;
        var contextSize = 8192;

        var apiConfigValue = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ApiConfig);
        if (!string.IsNullOrEmpty(apiConfigValue) && File.Exists(apiConfigValue))
        {
            var apiConfig = JObject.Parse(File.ReadAllText(apiConfigValue));

            apiEndpoint = apiConfig.Value<string>("apiEndpoint");
            apiKey = apiConfig.Value<string>("apiKey");
            apiModel = apiConfig.Value<string>("apiModel");
            contextSize = apiConfig.Value<int>("contextSize");
        }
        else
        {
            apiEndpoint = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ApiEndpoint);
            apiKey = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ApiKey);
            apiModel = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ApiModel);
            contextSize = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ContextSize);
        }

        if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
        {
            var apiConfigPath = Config.InteractiveApiConfigSetup();
            if (!string.IsNullOrEmpty(apiConfigPath))
            {
                var apiConfig = JObject.Parse(File.ReadAllText(apiConfigPath));

                apiEndpoint = apiConfig.Value<string>("apiEndpoint");
                apiKey = apiConfig.Value<string>("apiKey");
                apiModel = apiConfig.Value<string>("apiModel");
            }
        }

        if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
        {
            Console.Error.WriteLine("apiEndpoint, apiKey, and/or apiModel is null or empty.");
            return;
        }

        var toolsConfigValue = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ToolsConfig);
        if (string.IsNullOrEmpty(toolsConfigValue) || !File.Exists(toolsConfigValue))
        {
            toolsConfigValue = Config.InteractiveToolsConfigSetup();
        }

        var toolServerAddressValue = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ToolServerAddress);
        var toolServerPortValue = context.ParseResult.GetValueForOption(ConsoleAgent.Options.ToolServerPort);

        var persistent = context.ParseResult.GetValueForOption(ConsoleAgent.Options.Persistent);
        string workingDirectoryValue = context.ParseResult.GetValueForOption(ConsoleAgent.Options.WorkingDirectory) ?? Environment.CurrentDirectory;
        string storageDirectoryValue = context.ParseResult.GetValueForOption(ConsoleAgent.Options.StorageDirectory) ?? Environment.CurrentDirectory;

        string? systemPrompt = Prompts.DefaultSystemPrompt;
        var systemPromptFileValue = context.ParseResult.GetValueForOption(ConsoleAgent.Options.SystemPromptFile);
        if (!string.IsNullOrEmpty(systemPromptFileValue) && File.Exists(systemPromptFileValue))
        {
            systemPrompt = File.ReadAllText(systemPromptFileValue);
        }

        var agentId = context.ParseResult.GetValueForOption(ConsoleAgent.Options.AgentId) ?? apiModel;
        var sessionId = context.ParseResult.GetValueForOption(ConsoleAgent.Options.SessionId);

        var consoleCommunication = new ConsoleCommunication();

        var agent = await CreateAgent(loggerFactory, consoleCommunication,
            apiEndpoint, apiKey, apiModel, contextSize,
            agentId, workingDirectoryValue, storageDirectoryValue, persistent, systemPrompt, sessionId,
            toolsFilePath: toolsConfigValue, toolServerAddress: toolServerAddressValue, toolServerPort: toolServerPortValue);

        var logger = loggerFactory.CreateLogger(nameof(ConsoleAgent));

        agent.StreamOutput = true;
        agent.PreWaitForContent = () => { Console.Write("> "); };
        agent.llmApi.PostParseUsage += (usage) => { Console.WriteLine(); logger.LogInformation("PromptTokens: {PromptTokens}, CompletionTokens: {UsageCompletionTokens}, TotalTokens: {UsageTotalTokens}, Context Used: {ContextUsedPercent}", usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, ((double)usage.TotalTokens / agent.llmApi.ContextSize).ToString("P")); };

        var cancellationToken = context.GetCancellationToken();

        await agent.Run(cancellationToken);
    }

    private static async Task<LlmAgent> CreateAgent(
        ILoggerFactory loggerFactory, IAgentCommunication agentCommunication,
        string apiEndpoint, string apiKey, string apiModel, int contextSize,
        string agentId, string workingDirectory, string storageDirectory, bool persistent = false, string? systemPrompt = null, string? sessionId = null,
        string? toolsFilePath = null, string? toolServerAddress = null, int toolServerPort = 5000)
    {
        var llmApi = new LlmApiOpenAi(loggerFactory, apiEndpoint, apiKey, apiModel)
        {
            ContextSize = contextSize
        };

        var agent = new LlmAgent(agentId, llmApi, agentCommunication)
        {
            Persistent = persistent,
            PersistentMessagesPath = storageDirectory,
            SessionId = sessionId
        };

        if (!Path.Exists(workingDirectory))
        {
            Directory.CreateDirectory(workingDirectory);
        }

        if (!Path.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }

        var stateDatabase = new StateDatabase(loggerFactory, Path.Join(storageDirectory, $"{agentId}.db"));
        agent.StateDatabase = stateDatabase;

        Session? session = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            session = stateDatabase.GetSession(sessionId);
            if (session == null)
            {
                session = new Session
                {
                    SessionId = sessionId,
                    Status = "New"
                };

                stateDatabase.CreateSession(session);
            }
        }

        Tool[]? tools = null;

        if (!string.IsNullOrEmpty(toolServerAddress))
        {
            try
            {
                var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(IPAddress.Parse(toolServerAddress), toolServerPort);
                var stream = tcpClient.GetStream();

                var rpc = new JsonRpc(stream);

                rpc.AddLocalRpcTarget(agentCommunication);
                rpc.AddLocalRpcTarget<ILlmApiMessageProvider>(llmApi, null);

                rpc.StartListening();

                var jsonRpcToolService = rpc.Attach<IJsonRpcToolService>();

                var toolNames = await jsonRpcToolService.GetToolNames();

                var remoteTools = new RemoteTool[toolNames.Length];
                for (int i = 0; i < remoteTools.Length; i++)
                {
                    remoteTools[i] = new RemoteTool(toolNames[i], jsonRpcToolService);
                }

                tools = remoteTools;
            }
            catch
            {
                tools = null;
            }
        }

        if (tools == null && !string.IsNullOrEmpty(toolsFilePath) && File.Exists(toolsFilePath))
        {
            var toolEventBus = new ToolEventBus();
            var toolsFile = JObject.Parse(File.ReadAllText(toolsFilePath));
            var toolFactory = new ToolFactory(loggerFactory, toolsFile);

            toolFactory.Register(agentCommunication);
            toolFactory.Register(loggerFactory);
            toolFactory.Register<ILlmApiMessageProvider>(llmApi);
            toolFactory.Register<IToolEventBus>(toolEventBus);
            toolFactory.Register(stateDatabase);

            toolFactory.AddParameter("basePath", workingDirectory);
            toolFactory.AddParameter("storageDirectory", storageDirectory);

            tools = toolFactory.Load(session, stateDatabase);

            agent.ToolEventBus = toolEventBus;
        }

        if (tools != null)
        {
            agent.AddTool(tools);
        }

        if (persistent)
        {
            agent.LoadMessages();
        }

        if (agent.llmApi.Messages.Count == 0 && !string.IsNullOrEmpty(systemPrompt))
        {
            agent.llmApi.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));
        }

        return agent;
    }
}
