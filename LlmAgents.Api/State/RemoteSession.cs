namespace LlmAgents.Api.State;

using System.Text.Json;
using LlmAgents.Agents;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using Microsoft.AspNetCore.SignalR.Client;

public class RemoteSession : Session
{
    public readonly LlmAgent LlmAgent;

    public RemoteSession(HubConnection hubConnection, LlmAgent llmAgent, string sessionId, SessionDatabase sessionDatabase)
        : base(sessionId, sessionDatabase)
    {
        HubConnection = hubConnection;
        LlmAgent = llmAgent;
    }

    public HubConnection HubConnection { get; set; }

    public async override Task<string?> GetState(string key)
    {
        try
        {
            return await HubConnection.InvokeAsync<string?>("GetState", SessionId, key);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get state from manager: {ex.Message}");
            return null;
        }
    }

    public async override Task SetState(string key, string value)
    {
        try
        {
            await HubConnection.InvokeAsync("SetState", SessionId, key, value);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set state to manager: {ex.Message}");
        }
    }

    public async override Task Load()
    {
        await HubConnection.InvokeAsync("Register", LlmAgent.Id, SessionId, LlmAgent.SessionCapability.Persistent, CancellationToken.None);
        await base.Load();
    }

    protected override async Task LoadMessages()
    {
        List<ChatCompletionMessageParam>? remoteMessages = null;
        DateTime? remoteLastUpdated = null;
        try
        {
            var remoteMessagesJson = await HubConnection.InvokeAsync<string>("GetMessages", SessionId);
            if (!string.IsNullOrEmpty(remoteMessagesJson))
            {
                remoteMessages = JsonSerializer.Deserialize<List<ChatCompletionMessageParam>>(remoteMessagesJson);
            }

            remoteLastUpdated = DateTime.Parse(await HubConnection.InvokeAsync<string>("GetLastUpdated", SessionId));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load from manager: {ex.Message}");
        }

        if (remoteMessages == null || remoteLastUpdated == null)
        {
            return;
        }

        if (remoteLastUpdated > LastActive)
        {
            messages.Clear();
            AddMessages(remoteMessages);
        }
        else
        {
            await SaveMessages();
        }
    }

    protected async override Task SaveMessages()
    {
        // TODO: right now just add the last message, there better logic to detect
        // specific changes and only replicate that
        try
        {
            var messagesJson = JsonSerializer.Serialize(messages);
            await HubConnection.InvokeAsync("SaveMessages", SessionId, messagesJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save to manager: {ex.Message}");
        }
    }
}
