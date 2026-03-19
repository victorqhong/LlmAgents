using System.Text.Json;
using LlmAgents.Communication;
using LlmAgents.Configuration;
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
        LlmApiOpenAiParameters llmApiParameters,
        LlmAgentParameters llmAgentParameters,
        ToolParameters toolParameters,
        SessionParameters sessionParameters)
    {
        var llmApi = new LlmApiOpenAi(loggerFactory, llmApiParameters);
        var agent = new LlmAgent(llmAgentParameters, llmApi, agentCommunication, loggerFactory);

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

        Session? session = null;
        if (!string.IsNullOrEmpty(sessionParameters.SessionId))
        {
            session = stateDatabase.GetSession(sessionParameters.SessionId);
            if (session == null)
            {
                session = Session.New(sessionParameters.SessionId);
                stateDatabase.CreateSession(session);
            }
        }
        else
        {
            if (llmAgentParameters.Persistent)
            {
                session = stateDatabase.GetLatestSession();
            }

            if (session == null)
            {
                session = Session.New();
                stateDatabase.CreateSession(session);
            }
        }

        session.PersistentMessagesPath = llmAgentParameters.StorageDirectory;

        if (llmAgentParameters.Persistent)
        {
            session.Load();
        }
        else if (!string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            var textContent = new ChatCompletionMessageParamSystem
            {
                Content = new ChatCompletionMessageParamContentString { Content = File.ReadAllText(sessionParameters.SystemPromptFile) },
            };
            session.AddMessages([textContent]);
        }

        agent.LoadSession(session, stateDatabase);

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

                    httpClient.DefaultRequestHeaders.Add("X-Session-Id", session.SessionId);
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

            toolFactory.AddParameter("basePath", sessionParameters.WorkingDirectory);
            toolFactory.AddParameter("storageDirectory", llmAgentParameters.StorageDirectory);

            var toolsFile = JsonSerializer.Deserialize<ToolsConfig>(File.ReadAllText(toolParameters.ToolsConfig));
            if (toolsFile != null)
            {
                tools.AddRange(toolFactory.Load(toolsFile, session, stateDatabase));
            }

            agent.ToolEventBus = toolEventBus;
        }

        if (tools.Count > 0)
        {
            agent.AddTool(tools.ToArray());
        }

        return agent;
    }

    private static async Task<IEnumerable<Tool>> CreateMcpTools(IClientTransport clientTransport, ToolFactory toolFactory)
    {
        var client = await McpClientFactory.CreateAsync(clientTransport);
        var tools = await client.ListToolsAsync();
        return tools.Select(tool => new McpTool(tool, client, toolFactory));
    }
}
