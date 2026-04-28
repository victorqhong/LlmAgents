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
    public static async Task<HubConnection?> ConfigureAgentHub(this LlmAgent agent, Uri agentManagerUrl, IAgentCommunication agentCommunication, ILogger logger)
    {
        var token = await Login.GetHubLoginToken(agentCommunication, agentManagerUrl, logger, CancellationToken.None);
        if (string.IsNullOrEmpty(token))
        {
            await agentCommunication.SendMessage("Could not login to AgentManager", true);
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
                        return await Login.GetHubLoginToken(agentCommunication, agentManagerUrl, logger, CancellationToken.None);
                    });
                };
            })
            .WithStatefulReconnect()
            .WithAutomaticReconnect()
            .Build();

        hub.Reconnected += async connectionId =>
        {
            logger.LogInformation("Reconnected to hub");
            await hub.InvokeAsync("Register", agent.Id, agent.SessionCapability.Session.SessionId, agent.SessionCapability.Persistent, CancellationToken.None);
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

        if (retry)
        {
            try
            {
                await hub.StartAsync(CancellationToken.None);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Exception while connecting to hub");
                return null;
            }
        }

        agent.PreWaitForContent += async () =>
        {
            await hub.InvokeAsync("UpdateStatus", agent.SessionCapability.Session.SessionId, "WAITING", CancellationToken.None);
        };
        agent.PostParseUsage += async (usage) =>
        {
            await hub.InvokeAsync("Log", agent.SessionCapability.Session.SessionId, "Usage", $"{{ \"PromptTokens\": {usage.PromptTokens}, \"CompletionTokens\": {usage.CompletionTokens}, \"TotalTokens\": {usage.TotalTokens} }}", "INFO", CancellationToken.None);
        };

        agent.ToolCallCapability.ToolCalled += async (tool, arguments, result) =>
        {
            await hub.InvokeAsync("Log", agent.SessionCapability.Session.SessionId, "Tool", $"{{ \"Name\": \"{tool}\", \"Arguments\": {JsonSerializer.Serialize(arguments)}, \"Result\": {JsonSerializer.Serialize(result)} }}", "INFO", CancellationToken.None);
        };
        agent.PostReceiveContent += async () =>
        {
            await hub.InvokeAsync("UpdateStatus", agent.SessionCapability.Session.SessionId, "WORKING", CancellationToken.None);
        };

        await hub.InvokeAsync("Register", agent.Id, agent.SessionCapability.Session.SessionId, agent.SessionCapability.Persistent, CancellationToken.None);

        var remoteSession = new RemoteSession(agent.SessionCapability.Session.SessionId, hub, agent.SessionCapability.Session.SessionDatabase);
        await remoteSession.Load();
        await agent.SessionCapability.Load(remoteSession, CancellationToken.None);

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
