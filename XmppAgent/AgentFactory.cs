using System.Collections.Concurrent;
using LlmAgents.Agents;
using LlmAgents.Agents.Capabilities;
using LlmAgents.Api.Extensions;
using LlmAgents.Configuration;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using XmppAgent.Communication;
using XmppAgent.Xmpp;

namespace XmppAgent;

internal static class AgentFactory
{
    public static async Task RunAgent(LlmApiConfig apiConfig, LlmAgentParameters agentParameters, ToolParameters toolParameters, SessionParameters sessionParameters, XmppConfig xmppParameters, CancellationToken cancellationToken = default)
    {
        try
        {
            var xmppTransport = new XmppTransport(
                xmppParameters.XmppUsername, xmppParameters.XmppDomain, xmppParameters.XmppPassword, trustHost: xmppParameters.XmppTrustHost)
            {
                MessageBatchSize = 4000
            };

#if DEBUG
            xmppTransport.Debug = true;
#endif
            await xmppTransport.Initialize();

            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            var agent = await LlmAgentFactory.CreateAgent(loggerFactory, apiConfig, agentParameters, toolParameters, sessionParameters);

            var logger = loggerFactory.CreateLogger<LlmAgent>();
            agent.ConfigureAssistantResponseWork = (agent, work) =>
            {
                work.AssistantMessagePrefix = string.Empty;
                work.OutputNewLine = false;

                work.PostParseUsage += (session, usage) =>
                {
                    logger.LogInformation("Session: {SessionId}, PromptTokens: {PromptTokens}, CompletionTokens: {CompletionTokens}, TotalTokens: {TotalTokens}, Context Used: {ContextUsed}", session.SessionId, usage.PromptTokens, usage.CompletionTokens, usage.TotalTokens, ((double)usage.TotalTokens / agent.llmApi.ApiConfig.ContextSize).ToString("P"));
                };

                work.PreGetResponse = session => Task.Run(async () =>
                {
                    if (agent.SessionCapability.GetSessionCommunication(session) is XmppCommunication xmppCommunication)
                    {
                        await xmppTransport.SendComposing(xmppCommunication.TargetJid);
                    }
                });
                work.PostSendMessage = session => Task.Run(async () =>
                {
                    if (agent.SessionCapability.GetSessionCommunication(session) is XmppCommunication xmppCommunication)
                    {
                        await xmppTransport.SendActive(xmppCommunication.TargetJid);
                    }
                });
            };

            var handles = new ConcurrentDictionary<XmppMessageMetadata, SessionCapability.SessionHandle>();
            async Task<SessionCapability.SessionHandle> GetHandle(XmppMessageMetadata metadata)
            {
                if (!handles.TryGetValue(metadata, out SessionCapability.SessionHandle handle))
                {
                    var sessionMetadata = new SessionCapability.SessionMetadata("Xmpp", metadata.BareJid, $"{agent.Id}--{metadata.BareJid}");
                    var xmppCommunication = new XmppCommunication(metadata.BareJid, xmppTransport);
                    handle = await agent.SessionCapability.CreateSession(sessionMetadata, xmppCommunication, cancellationToken);
                    if (!handles.TryAdd(metadata, handle))
                    {
                        throw new Exception();
                    }

                    var session = agent.SessionCapability.GetSession(handle);
                    var newSession = agent.SessionCapability.SessionDatabase.GetSession(session.SessionId) == null;
                    await LlmAgentFactory.InitializeSession(session, agent, agentParameters, sessionParameters, newSession, cancellationToken);
                }

                return handle;
            }

            xmppTransport.OnMessageContent += async (metadata, messageContent) =>
            {
                var handle = await GetHandle(metadata);

                var xmppCommunication = (XmppCommunication)agent.SessionCapability.GetSessionCommunication(handle);
                if (!xmppCommunication.TryAcceptIncomingMessage(metadata, messageContent))
                {
                    await agent.PostInput(handle, [messageContent], cancellationToken);
                }
            };

            if (agentParameters.AgentManagerUrl != null)
            {
                var handle = await GetHandle(new XmppMessageMetadata(xmppParameters.XmppTargetJid));
                var communication = agent.SessionCapability.GetSessionCommunication(handle);

                await agent.ConfigureAgentHub(agentParameters.AgentManagerUrl, communication, logger);
            }

            await agent.Run(cancellationToken);
        }
        catch (TaskCanceledException) { }
        catch (Exception e)
        {
            Console.WriteLine($"Error loading agent for: {agentParameters.AgentId}");
            Console.WriteLine(e);
        }
    }
}
