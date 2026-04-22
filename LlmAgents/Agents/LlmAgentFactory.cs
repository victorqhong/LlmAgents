using System.Text.Json;
using LlmAgents.Communication;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.Content;
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
    public static Func<FactoryParameters, Task<LlmApiOpenAi>> CreateLlmApi { get; set; } = Defaults.CreateLlmApi;
    public static Func<FactoryParameters, Task<Session>> CreateSession { get; set; } = Defaults.CreateSession;
    public static Func<LlmAgent, FactoryParameters, Task<Tool[]>> CreateTools { get; set; } = Defaults.CreateTools;

    private static ILogger Log;

    public static async Task<LlmAgent> CreateAgent(
        ILoggerFactory loggerFactory,
        IAgentCommunication agentCommunication,
        LlmApiConfig llmApiParameters,
        LlmAgentParameters llmAgentParameters,
        ToolParameters toolParameters,
        SessionParameters sessionParameters)
    {
        Log ??= loggerFactory.CreateLogger("LlmAgentFactory");

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

        var llmApi = await CreateLlmApi(factoryParameters);
        var agent = new LlmAgent(llmAgentParameters, llmApi, agentCommunication, loggerFactory);

        var session = await CreateSession(factoryParameters);
         agent.SessionCapability.OutputMessagesOnLoad = sessionParameters.OutputMessagesOnLoad;
        await agent.SessionCapability.Load(session, CancellationToken.None);

        var tools = await CreateTools(agent, factoryParameters);
        agent.ToolCallCapability.AddTool(tools);

        return agent;
    }

    private static class Defaults
    {
        public static Task<LlmApiOpenAi> CreateLlmApi(FactoryParameters factoryParameters)
        {
            var loggerFactory = factoryParameters.loggerFactory;
            var llmApiParameters = factoryParameters.apiConfig;
            var llmApi = llmApiParameters.Llamacpp != null ? new LlmApiLlamacpp(loggerFactory, llmApiParameters) : new LlmApiOpenAi(loggerFactory, llmApiParameters);
            return Task.FromResult(llmApi);
        }

        public static async Task<Session> CreateSession(FactoryParameters factoryParameters)
        {
            var llmAgentParameters = factoryParameters.agentParameters;
            var sessionParameters = factoryParameters.sessionParameters;
            var stateDatabase = factoryParameters.stateDatabase;

            var sessionDatabase = new SessionDatabase(stateDatabase);

            var newSession = false;
            Session? session = null;
            if (!string.IsNullOrEmpty(sessionParameters.Session))
            {
                if (string.Equals(sessionParameters.Session, "new"))
                {
                    session = new Session(Guid.NewGuid().ToString(), sessionDatabase);
                    newSession = true;
                }
                else if (string.Equals(sessionParameters.Session, "choose"))
                {
                    var sessions = sessionDatabase.GetSessions();
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        await factoryParameters.agentCommunication.SendMessage($"{i + 1}) {sessions[i].SessionId} (Last Active: {sessions[i].LastActive.ToLocalTime()})", true);
                    }

                    await factoryParameters.agentCommunication.SendMessage("> ", false);
                    var content = await factoryParameters.agentCommunication.WaitForContent();
                    if (content is MessageContentText[] textContent && !string.IsNullOrEmpty(textContent[0].Text) && int.TryParse(textContent[0].Text, out var sessionChoice)) 
                    {
                        session = sessions[sessionChoice - 1];
                    }
                }
                else if (string.Equals(sessionParameters.Session, "latest"))
                {
                    session = sessionDatabase.GetLatestSession();
                    if (session == null)
                    {
                        session = new Session(Guid.NewGuid().ToString(), sessionDatabase);
                        newSession = true;
                    }
                }
                else
                {
                    session = sessionDatabase.GetSession(sessionParameters.Session);
                    if (session == null)
                    {
                        session = new Session(sessionParameters.Session, sessionDatabase);
                        newSession = true;
                    }
                }
            }

            if (session == null)
            {
                session = new Session(Guid.NewGuid().ToString(), sessionDatabase);
                newSession = true;
            }

            if (newSession && llmAgentParameters.Persistent)
            {
                sessionDatabase.CreateSession(session);
            }

            session.PersistentMessagesPath = llmAgentParameters.StorageDirectory;

            await session.Load();

            if (newSession && !string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
            {
                var textContent = new ChatCompletionMessageParamSystem
                {
                    Content = new ChatCompletionMessageParamContentString { Content = File.ReadAllText(sessionParameters.SystemPromptFile) }
                };

                session.AddMessages([textContent]);
            }

            return session;
        }

        public static async Task<Tool[]> CreateTools(LlmAgent agent, FactoryParameters factoryParameters)
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
                            Arguments = stdioMcpServer.Args,
                            EnvironmentVariables = stdioMcpServer.Env
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
                toolFactory.Register(agent);

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
            try
            {
                var client = await McpClientFactory.CreateAsync(clientTransport);
                var tools = await client.ListToolsAsync();
                return tools.Select(tool => new McpTool(tool, client, toolFactory));
            }
            catch (Exception e)
            {
                Log.LogError(e, "Could not create McpTools");
            }

            return [];
        }
    }

    public class FactoryParameters
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
