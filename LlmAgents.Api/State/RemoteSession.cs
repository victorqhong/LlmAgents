namespace LlmAgents.Api.State;

using System.Text.Json;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using Microsoft.AspNetCore.SignalR.Client;

public class RemoteSession : Session
{
    public RemoteSession(string sessionId, HubConnection hubConnection, SessionDatabase sessionDatabase)
        : base(sessionId, sessionDatabase)
    {
        HubConnection = hubConnection;
    }

    public HubConnection HubConnection { get; set; }

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
            var messagesJson = JsonSerializer.Serialize(messages.TakeLast(1));
            await HubConnection.InvokeAsync("AddMessages", SessionId, messagesJson);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save to manager: {ex.Message}");
        }
    }
}
