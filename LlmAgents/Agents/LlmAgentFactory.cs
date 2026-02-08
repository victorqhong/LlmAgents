namespace LlmAgents.Agents;

using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Newtonsoft.Json.Linq;

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
        var agent = new LlmAgent(llmAgentParameters, llmApi, agentCommunication)
        {
            SessionId = sessionParameters.SessionId,
        };

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

        List<Tool> tools = [];

        if (Uri.TryCreate($"http://{toolParameters.ToolServerAddress}:{toolParameters.ToolServerPort}", UriKind.Absolute, out var toolServerUri))
        {
            var clientTransport = new SseClientTransport(new SseClientTransportOptions()
            {
                Endpoint = toolServerUri
            });

            var client = await McpClientFactory.CreateAsync(clientTransport);
            var mcpTools = await client.ListToolsAsync();

            var toolFactory = new ToolFactory(loggerFactory);

            tools.AddRange(mcpTools.Select(mcpClientTool => new McpTool(mcpClientTool, client, toolFactory)).ToArray());
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

        if (agent.RenderConversation().Count == 0 && !string.IsNullOrEmpty(sessionParameters.SystemPromptFile))
        {
            agent.AddMessages([JObject.FromObject(new { role = "system", content = sessionParameters.SystemPromptFile })]);
        }

        return agent;
    }
}
