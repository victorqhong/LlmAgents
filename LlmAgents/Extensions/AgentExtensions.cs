using System.Text.Json;
using LlmAgents.Agents;
using Microsoft.AspNetCore.SignalR.Client;

namespace LlmAgents.Extensions;

public static class AgentExtensions
{
    public static async Task ConfigureAgentHub(this LlmAgent agent, HubConnection hub)
    {
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
            await hub.InvokeAsync("Log", agent.Session.SessionId, "Tool", $"{{ \"Name\": \"{tool}\", \"Arguments\": {arguments}, \"Result\": {result} }}", "INFO", CancellationToken.None);
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
    }
}
