using System.Text.Json;
using LlmAgents.Agents;
using LlmAgents.Api.GitHub;
using LlmAgents.Communication;
using Microsoft.AspNetCore.SignalR.Client;

namespace LlmAgents.Api.Extensions;

public static class AgentExtensions
{
    public static async Task<HubConnection> ConfigureAgentHub(this LlmAgent agent, Uri agentManagerUrl, IAgentCommunication agentCommunication)
    {
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
            .WithAutomaticReconnect()
            .Build();

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
        hub.Reconnected += async connectionId =>
        {
            await hub.InvokeAsync("Register", agent.Id, agent.Session.SessionId, agent.Persistent, CancellationToken.None);
        };

        return hub;
    }
}
