using LlmAgents.Agents;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using XmppAgent.Communication;
using XmppAgent.Logging;

namespace XmppAgent;

internal static class AgentFactory
{
    public static Task RunAgent(LlmApiOpenAiParameters apiParameters, LlmAgentParameters agentParameters, ToolParameters toolParameters, SessionParameters sessionParameters, XmppParameters xmppParameters, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            try
            {
                var xmppCommunication = new XmppCommunication(
                    xmppParameters.XmppUsername, xmppParameters.XmppDomain, xmppParameters.XmppPassword, trustHost: xmppParameters.XmppTrustHost)
                {
                    TargetJid = xmppParameters.XmppTargetJid
                };
#if DEBUG
                xmppCommunication.Debug = true;
#endif
                await xmppCommunication.Initialize();

                using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new XmppLoggerProvider(xmppCommunication)));

                var agent = await LlmAgentFactory.CreateAgent(loggerFactory, xmppCommunication, apiParameters, agentParameters, toolParameters, sessionParameters);

                agent.PostReceiveContent = () => Task.Run(() => xmppCommunication.SendComposing());
                agent.PostSendMessage = () => Task.Run(() => xmppCommunication.SendActive());

                await agent.Run(cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error loading agent for: {agentParameters.AgentId}");
                Console.WriteLine(e);
            }

        }, cancellationToken);
    }
}