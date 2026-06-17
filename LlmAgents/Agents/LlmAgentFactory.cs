using LlmAgents.Configuration;
using LlmAgents.LlmApi.Llamacpp;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Agents;

public static class LlmAgentFactory
{
    public static async Task<LlmAgent> CreateAgent(
        ILoggerFactory loggerFactory,
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

        var llmApi = llmApiParameters.Llamacpp != null ? new LlmApiLlamacpp(loggerFactory, llmApiParameters) : new LlmApiOpenAi(loggerFactory, llmApiParameters);
        var agent = new LlmAgent(llmAgentParameters, llmApi, loggerFactory);

        agent.ToolCallCapability.ToolFactory.Register(agent);
        agent.ToolCallCapability.ToolFactory.AddParameter("basePath", sessionParameters.WorkingDirectory ?? Environment.CurrentDirectory);
        agent.ToolCallCapability.ToolFactory.AddParameter("storageDirectory", llmAgentParameters.StorageDirectory);

        var tools = await agent.ToolCallCapability.CreateTools(toolParameters);
        agent.ToolCallCapability.AddToolDefinition(tools);

        return agent;
    }

    public static async Task InitializeSession(Session session, LlmAgent agent, LlmAgentParameters agentParameters, SessionParameters sessionParameters, bool newSession, CancellationToken cancellationToken)
    {
        if (newSession && agentParameters.Persistent)
        {
            var sessionDatabase = agent.SessionCapability.SessionDatabase;
            if (sessionDatabase.GetSession(session.SessionId) == null)
            {
                sessionDatabase.CreateSession(session);
            }
        }

        session.PersistentMessagesPath = agentParameters.StorageDirectory;

        await session.Load(cancellationToken);

        if (newSession && !string.IsNullOrEmpty(sessionParameters.SystemPromptFile) && File.Exists(sessionParameters.SystemPromptFile))
        {
            var textContent = new ChatCompletionMessageParamSystem
            {
                Content = new ChatCompletionMessageParamContentString { Content = File.ReadAllText(sessionParameters.SystemPromptFile) }
            };

            session.AddMessages([textContent]);
        }

        await agent.ToolCallCapability.InitializeSessionTools(session);
    }
}
