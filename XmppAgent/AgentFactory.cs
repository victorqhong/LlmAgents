using LlmAgents.Agents;
using LlmAgents.Agents.Work;
using LlmAgents.Api.Extensions;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using XmppAgent.Communication;

namespace XmppAgent;

internal static class AgentFactory
{
    public static async Task RunAgent(LlmApiOpenAiParameters apiParameters, LlmAgentParameters agentParameters, ToolParameters toolParameters, SessionParameters sessionParameters, XmppConfig xmppParameters, CancellationToken cancellationToken = default)
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

            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

            var agent = await LlmAgentFactory.CreateAgent(loggerFactory, xmppCommunication, apiParameters, agentParameters, toolParameters, sessionParameters);

            agent.PreGetResponse = () => Task.Run(() => xmppCommunication.SendComposing());
            agent.PostSendMessage = () => Task.Run(() => xmppCommunication.SendActive());

            agent.CreateAssistantResponseWork = agent =>
            {
                return new GetAssistantResponseWork(loggerFactory, agent)
                {
                    AssistantMessagePrefix = string.Empty,
                    OutputNewLine = false
                };
            };

            if (agentParameters.AgentManagerUrl != null)
            {
                await agent.ConfigureAgentHub(agentParameters.AgentManagerUrl, xmppCommunication);
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
