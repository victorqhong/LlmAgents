using System.Text.Json;
using LlmAgents.Communication;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.Llamacpp;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace LlmAgents.Agents;

public static class LlmAgentFactory
{
    public static async Task<LlmAgent> CreateAgent(
        ILoggerFactory loggerFactory, IAgentCommunication agentCommunication,
        LlmApiConfig llmApiParameters,
        LlmAgentParameters llmAgentParameters,
        ToolParameters toolParameters,
        SessionParameters sessionParameters)
    {
        if (string.IsNullOrEmpty(sessionParameters.WorkingDirectory))
        {
            sessionParameters.WorkingDirectory = Environment.CurrentDirectory;
        }
        else if (!Path.Exists(sessionParameters.WorkingDirectory))
        {
            Directory.CreateDirectory(sessionParameters.WorkingDirectory);
        }

        if (!Path.Exists(llmAgentParameters.StorageDirectory))
        {
            Directory.CreateDirectory(llmAgentParameters.StorageDirectory);
        }

        var stateDatabase = new StateDatabase(loggerFactory, Path.Join(llmAgentParameters.StorageDirectory, $"{llmAgentParameters.AgentId}.db"));

        var factoryParameters = new FactoryParameters
        {
            loggerFactory = loggerFactory,
            agentCommunication = agentCommunication,
            apiConfig = llmApiParameters,
            agentParameters = llmAgentParameters,
            toolParameters = toolParameters,
            sessionParameters = sessionParameters,
            stateDatabase = stateDatabase
        };

        var llmApi = CreateLlmApi(factoryParameters);
        var agent = new LlmAgent(llmAgentParameters, llmApi, agentCommunication, loggerFactory);

        var session = await CreateSession(factoryParameters);
        await agent.SessionCapability.Load(session, CancellationToken.None);

        var tools = await CreateTools(agent, factoryParameters);
        agent.ToolCallCapability.AddTool(tools);

        return agent;
    }

    private static LlmApiOpenAi CreateLlmApi(FactoryParameters factoryParameters)
    {
        var loggerFactory = factoryParameters.loggerFactory;
        var llmApiParameters = factoryParameters.apiConfig;
        return llmApiParameters.Llamacpp != null ? new LlmApiLlamacpp(loggerFactory, llmApiParameters) : new LlmApiOpenAi(loggerFactory, llmApiParameters);
    }

    private static async Task<Session> CreateSession(FactoryParameters factoryParameters)
    {
        var llmAgentParameters = factoryParameters.agentParameters;
        var sessionParameters = factoryParameters.sessionParameters;
        var stateDatabase = factoryParameters.stateDatabase;

        var sessionDatabase = new SessionDatabase(stateDatabase);

        Session? session = null;
        if (!string.IsNullOrEmpty(sessionParameters.SessionId))
        {
            session = sessionDatabase.GetSession(sessionParameters.SessionId);
        }
        else if (llmAgentParameters.Persistent)
        {
            session = sessionDatabase.GetLatestSession();
        }

        if (session == null)
        {
            session = new Session(sessionParameters.SessionId ?? Guid.NewGuid().ToString(), sessionDatabase);
            sessionDatabase.CreateSession(session);
        }

        session.PersistentMessagesPath = llmAgentParameters.StorageDirectory;

        if (llmAgentParameters.Persistent)
        {
            await session.Load();
        }
        else if (!string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            var textContent = new ChatCompletionMessageParamSystem
            {
                Content = new ChatCompletionMessageParamContentString { Content = File.ReadAllText(sessionParameters.SystemPromptFile) }
            };

            session.AddMessages([textContent]);
        }

        return session;
    }

    private static async Task<Tool[]> CreateTools(LlmAgent agent, FactoryParameters factoryParameters)
    {
        var loggerFactory = factoryParameters.loggerFactory;
        var llmAgentParameters = factoryParameters.agentParameters;
        var sessionParameters = factoryParameters.sessionParameters;
        var toolParameters = factoryParameters.toolParameters;
        var agentCommunication = factoryParameters.agentCommunication;
        var stateDatabase = factoryParameters.stateDatabase;

        List<Tool> tools = [];

        if (File.Exists(toolParameters.McpConfigPath) && JsonSerializer.Deserialize<McpConfig>(File.ReadAllText(toolParameters.McpConfigPath)) is McpConfig mcpConfig)
        {
            foreach (var kvp in mcpConfig.Servers)
            {
                var mcpServer = kvp.Value;
                if (mcpServer is McpServerConfigHttp httpMcpServer)
                {
                    if (!Uri.TryCreate(httpMcpServer.Url, UriKind.Absolute, out var toolServerUri))
                    {
                        continue;
                    }

                    var httpClient = new HttpClient();
                    if (httpMcpServer.Headers != null)
                    {
                        foreach (var header in httpMcpServer.Headers)
                        {
                            httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }
                    }

                    httpClient.DefaultRequestHeaders.Add("X-Session-Id", agent.SessionCapability.Session.SessionId);
                    httpClient.DefaultRequestHeaders.Add("X-Agent-Id", agent.Id);

                    var clientTransport = new SseClientTransport(
                        new SseClientTransportOptions { Endpoint = toolServerUri },
                        httpClient
                    );

                    var toolFactory = new ToolFactory(loggerFactory);
                    tools.AddRange(await CreateMcpTools(clientTransport, toolFactory));
                }
                else if (mcpServer is McpServerConfigStdio stdioMcpServer)
                {
                    var stdioTransport = new StdioClientTransport(new StdioClientTransportOptions
                    {
                        Command = stdioMcpServer.Command,
                        Arguments = stdioMcpServer.Args
                    });

                    var toolFactory = new ToolFactory(loggerFactory);
                    tools.AddRange(await CreateMcpTools(stdioTransport, toolFactory));
                }
            }
        }

        if (!string.IsNullOrEmpty(toolParameters.ToolsConfig) && File.Exists(toolParameters.ToolsConfig))
        {
            var toolEventBus = new ToolEventBus();
            var toolFactory = new ToolFactory(loggerFactory);

            toolFactory.Register(agentCommunication);
            toolFactory.Register(loggerFactory);
            toolFactory.Register<IToolEventBus>(toolEventBus);
            toolFactory.Register(stateDatabase);

            toolFactory.AddParameter("basePath", sessionParameters.WorkingDirectory ?? Environment.CurrentDirectory);
            toolFactory.AddParameter("storageDirectory", llmAgentParameters.StorageDirectory);

            var toolsFile = JsonSerializer.Deserialize<ToolsConfig>(File.ReadAllText(toolParameters.ToolsConfig));
            if (toolsFile != null)
            {
                tools.AddRange(await toolFactory.Load(toolsFile, agent.SessionCapability.Session));
            }

            agent.ToolCallCapability.ToolEventBus = toolEventBus;
        }

        return tools.ToArray();
    }

    private static async Task<IEnumerable<Tool>> CreateMcpTools(IClientTransport clientTransport, ToolFactory toolFactory)
    {
        var client = await McpClientFactory.CreateAsync(clientTransport);
        var tools = await client.ListToolsAsync();
        return tools.Select(tool => new McpTool(tool, client, toolFactory));
    }

    private class FactoryParameters
    {
        public required ILoggerFactory loggerFactory;
        public required IAgentCommunication agentCommunication;
        public required LlmApiConfig apiConfig;
        public required LlmAgentParameters agentParameters;
        public required ToolParameters toolParameters;
        public required SessionParameters sessionParameters;
        public required StateDatabase stateDatabase;
    }
}
