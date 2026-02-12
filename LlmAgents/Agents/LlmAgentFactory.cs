using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Agents;

public static class LlmAgentFactory
{
    public static LlmAgent InstantiateAgent(ILoggerFactory loggerFactory, IAgentCommunication agentCommunication,
        LlmApiOpenAiParameters llmApiParameters, LlmAgentParameters llmAgentParameters)
    {
        var llmApi = new LlmApiOpenAi(loggerFactory, llmApiParameters);
        var agent = new LlmAgent(llmAgentParameters, llmApi, agentCommunication);

        return agent;
    }

    public static async Task ConfigureAgent(LlmAgent agent, ILoggerFactory loggerFactory, IAgentCommunication agentCommunication, LlmAgentParameters llmAgentParameters, ToolParameters toolParameters, SessionParameters sessionParameters)
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
        agent.StateDatabase = stateDatabase;

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
            session = stateDatabase.GetLatestSession();
            if (session == null)
            {
                session = Session.New();
                stateDatabase.CreateSession(session);
            }
        }

        agent.Session = session;

        var toolFactory = new ToolFactory(loggerFactory);

        List<Tool> tools = [];

        if (File.Exists(toolParameters.McpConfigPath))
        {
            var mcpJson = JObject.Parse(File.ReadAllText(toolParameters.McpConfigPath));
            if (mcpJson.ContainsKey("servers") && mcpJson.Value<JObject>("servers") is JObject servers)
            {
                foreach (var property in servers.Properties())
                {
                    if (property.Value is not JObject server)
                    {
                        continue;
                    }

                    var type = server.Value<string>("type");
                    if (string.Equals(type, "http") && server.ContainsKey("url") && server.Value<string>("url") is string url)
                    {
                        if (!Uri.TryCreate(url, UriKind.Absolute, out var toolServerUri))
                        {
                            continue;
                        }

                        var httpClient = new HttpClient();
                        if (server.ContainsKey("headers") && server.Value<JObject>("headers") is JObject headers)
                        {
                            foreach (var header in headers.Properties())
                            {
                                httpClient.DefaultRequestHeaders.Add(header.Name, header.Value.Value<string>());
                            }
                        }

                        httpClient.DefaultRequestHeaders.Add("X-Session-Id", session.SessionId);
                        httpClient.DefaultRequestHeaders.Add("X-Agent-Id", agent.Id);

                        var clientTransport = new SseClientTransport(
                            new SseClientTransportOptions { Endpoint = toolServerUri },
                            httpClient
                        );

                        tools.AddRange(await CreateMcpTools(clientTransport, toolFactory));
                    }
                    else if (string.Equals(type, "stdio") && server.Value<string>("command") is string command && server.Value<JArray>("args") is JArray args)
                    {
                        var arguments = args.Select(token => token.Value<string>() ?? "").ToList();
                        var stdioTransport = new StdioClientTransport(new StdioClientTransportOptions
                        {
                            Command = command,
                            Arguments = arguments
                        });

                        tools.AddRange(await CreateMcpTools(stdioTransport, toolFactory));
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(toolParameters.ToolsConfig) && File.Exists(toolParameters.ToolsConfig))
        {
            var toolEventBus = new ToolEventBus();
            var toolsFile = JObject.Parse(File.ReadAllText(toolParameters.ToolsConfig));
            var toolFactory = new ToolFactory(loggerFactory, toolsFile);

            toolFactory.Register(agentCommunication);
            toolFactory.Register(loggerFactory);
            toolFactory.Register<IToolEventBus>(toolEventBus);
            toolFactory.Register(stateDatabase);

            toolFactory.AddParameter("basePath", sessionParameters.WorkingDirectory);
            toolFactory.AddParameter("storageDirectory", llmAgentParameters.StorageDirectory);

            var localTools = toolFactory.Load(session, stateDatabase);
            if (localTools != null)
            {
                tools.AddRange(localTools);
            }

            agent.ToolEventBus = toolEventBus;
        }

        if (tools.Count > 0)
        {
            agent.AddTool(tools.ToArray());
        }

        if (llmAgentParameters.Persistent)
        {
            agent.LoadMessages();
        }

        if (agent.RenderConversation().Count == 0 && !string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            agent.AddMessages([JObject.FromObject(new { role = "system", content = File.ReadAllText(sessionParameters.SystemPromptFile) })]);
        }
    }

    public static async Task<LlmAgent> CreateAgent(
        ILoggerFactory loggerFactory, IAgentCommunication agentCommunication,
        LlmApiOpenAiParameters llmApiParameters,
        LlmAgentParameters llmAgentParameters,
        ToolParameters toolParameters,
        SessionParameters sessionParameters)
    {
        var agent = InstantiateAgent(loggerFactory, agentCommunication, llmApiParameters, llmAgentParameters);
        await ConfigureAgent(agent, loggerFactory, agentCommunication, llmAgentParameters, toolParameters, sessionParameters);

        return agent;
    }

    private static async Task<IEnumerable<Tool>> CreateMcpTools(IClientTransport clientTransport, ToolFactory toolFactory)
    {
        var client = await McpClientFactory.CreateAsync(clientTransport);
        var tools = await client.ListToolsAsync();
        return tools.Select(tool => new McpTool(tool, client, toolFactory));
    }
}
