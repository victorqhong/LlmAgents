using System.Net;
using System.Text.Json;
using LlmAgents.Agents;
using LlmAgents.Api.GitHub;
using LlmAgents.Api.State;
using LlmAgents.Communication;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Api.Extensions;

public static class AgentExtensions
{
    public static async Task<HubConnection?> ConfigureAgentHub(this LlmAgent agent, Uri agentManagerUrl, SessionCommunication sessionCommunication, ILogger logger)
    {
        var token = await Login.GetHubLoginToken(sessionCommunication, agentManagerUrl, logger, CancellationToken.None);
        if (string.IsNullOrEmpty(token))
        {
            await sessionCommunication.SendMessage("Could not login to AgentManager", true);
            return null;
        }

        var hubUrl = new Uri(agentManagerUrl, "hubs/agent");
        var hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () =>
                {
                    return Task.Run(async () =>
                    {
                        return await Login.GetHubLoginToken(sessionCommunication, agentManagerUrl, logger, CancellationToken.None);
                    });
                };
            })
            .WithStatefulReconnect()
            .WithAutomaticReconnect()
            .Build();

        hub.Reconnected += async connectionId =>
        {
            logger.LogInformation("Reconnected to hub");
            await hub.InvokeAsync("UpdateAgentStatus", agent.Id, "RECONNECTED", CancellationToken.None);
        };

        hub.Closed += e =>
        {
            logger.LogInformation("Connection to hub closed: {message}", e?.Message ?? "no exception");
            return Task.CompletedTask;
        };

        hub.Reconnecting += e =>
        {
            logger.LogInformation("Reconnecting to hub: {message}", e?.Message ?? "no exception");
            return Task.CompletedTask;
        };
        
        var retry = false;
        while (retry)
        {
            try
            {
                await hub.StartAsync(CancellationToken.None);
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode == HttpStatusCode.Unauthorized)
                {
                    HubAuthTokenStore.ClearToken();
                    retry = true;
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Exception while connecting to hub");
                return null;
            }
        }

        agent.ConfigureAssistantResponseWork += (agent, work) =>
        {
            work.PostParseUsage += async (session, usage) =>
            {
                await hub.InvokeAsync("Log", session.SessionId, "Usage", $"{{ \"PromptTokens\": {usage.PromptTokens}, \"CompletionTokens\": {usage.CompletionTokens}, \"TotalTokens\": {usage.TotalTokens} }}", "INFO", CancellationToken.None);
            };
        };

        agent.ToolCallCapability.ToolCalled += async (session, tool, arguments, result) =>
        {
            await hub.InvokeAsync("Log", session.SessionId, "Tool", $"{{ \"Name\": \"{tool}\", \"Arguments\": {JsonSerializer.Serialize(arguments)}, \"Result\": {JsonSerializer.Serialize(result)} }}", "INFO", CancellationToken.None);
        };

        agent.PreProcessSession += async session =>
        {
            await hub.InvokeAsync("UpdateSessionStatus", session.SessionId, "WORKING", CancellationToken.None);
        };

        agent.PostProcessSession += async session =>
        {
            await hub.InvokeAsync("UpdateSessionStatus", session.SessionId, "WAITING", CancellationToken.None);
        };

        agent.SessionCapability.SessionFactory = async (sessionId, sessionDatabase, cancellationToken) =>
        {
            return new RemoteSession(hub, agent, sessionId, sessionDatabase);
        };

        _ = Task.Run(async () =>
        {
            var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
            while (await timer.WaitForNextTickAsync())
            {
                await hub.InvokeAsync("Ping");
            }
        });

        return hub;
    }
}
