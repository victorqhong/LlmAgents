using LlmAgents.Agents;
using LlmAgents.Agents.Work;
using LlmAgents.Api.GitHub;
using LlmAgents.Extensions;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using XmppAgent.Communication;

namespace XmppAgent;

internal static class AgentFactory
{
    public static async Task RunAgent(LlmApiOpenAiParameters apiParameters, LlmAgentParameters agentParameters, ToolParameters toolParameters, SessionParameters sessionParameters, XmppParameters xmppParameters, CancellationToken cancellationToken = default)
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
                return new GetAssistantResponseWork(agent)
                {
                    AssistantMessagePrefix = string.Empty,
                    OutputNewLine = false
                };
            };

            if (agentParameters.AgentManagerUrl != null)
            {
                var hubUrl = new Uri(agentParameters.AgentManagerUrl, "hubs/agent");
                var hub = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.AccessTokenProvider = () =>
                        {
                            return Task.Run(async () =>
                            {
                                return await Login.GetHubLoginToken(xmppCommunication, agentParameters.AgentManagerUrl, cancellationToken);
                            });
                        };
                    })
                    .WithAutomaticReconnect()
                    .Build();

                await hub.StartAsync(cancellationToken);
                await agent.ConfigureAgentHub(hub);
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
