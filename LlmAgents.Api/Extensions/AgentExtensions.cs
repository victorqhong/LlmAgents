using System.Text.Json;
using LlmAgents.Agents;
using LlmAgents.Api.GitHub;
using LlmAgents.Communication;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace LlmAgents.Api.Extensions;

public static class AgentExtensions
{
    public static async Task<HubConnection?> ConfigureAgentHub(this LlmAgent agent, Uri agentManagerUrl, IAgentCommunication agentCommunication, ILogger logger)
    {
        var token = await Login.GetHubLoginToken(agentCommunication, agentManagerUrl, CancellationToken.None);
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
                        return await Login.GetHubLoginToken(agentCommunication, agentManagerUrl, CancellationToken.None);
                    });
                };
            })
            .WithStatefulReconnect()
            .WithAutomaticReconnect()
            .Build();

        hub.Reconnected += async connectionId =>
        {
            logger.LogInformation("Reconnected to hub");
            await hub.InvokeAsync("Register", agent.Id, agent.Session.SessionId, agent.Persistent, CancellationToken.None);
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

        await hub.StartAsync(CancellationToken.None);

        agent.PreWaitForContent += async () =>
        {
            await hub.InvokeAsync("UpdateStatus", agent.Session.SessionId, "WAITING", CancellationToken.None);
        };
        agent.PostParseUsage += async (usage) =>
        {
            await hub.InvokeAsync("Log", agent.Session.SessionId, "Usage", $"{{ \"PromptTokens\": {usage.PromptTokens}, \"CompletionTokens\": {usage.CompletionTokens}, \"TotalTokens\": {usage.TotalTokens} }}", "INFO", CancellationToken.None);
        };
        agent.ToolCalled += async (tool, arguments, result) =>
        {
            await hub.InvokeAsync("Log", agent.Session.SessionId, "Tool", $"{{ \"Name\": \"{tool}\", \"Arguments\": {JsonSerializer.Serialize(arguments)}, \"Result\": {JsonSerializer.Serialize(result)} }}", "INFO", CancellationToken.None);
        };
        agent.PostRunWork += async work =>
        {
            if (work.Messages == null)
            {
                return;
            }

            await hub.InvokeAsync("AddMessages", agent.Session.SessionId, JsonSerializer.Serialize(work.Messages), CancellationToken.None);
        };
        agent.PostReceiveContent += async () =>
        {
            await hub.InvokeAsync("UpdateStatus", agent.Session.SessionId, "WORKING", CancellationToken.None);
        };

        await hub.InvokeAsync("Register", agent.Id, agent.Session.SessionId, agent.Persistent, CancellationToken.None);

        return hub;
    }
}
