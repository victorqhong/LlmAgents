using System.Text.Json;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.AspNetCore.SignalR.Client;

namespace LlmAgents.State;

public class Session
{
    private readonly static JsonSerializerOptions serializerOptions;

    static Session()
    {
        serializerOptions = new()
        {
            WriteIndented = true
        };
    }

    public required string SessionId;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    public string Metadata { get; set; } = string.Empty;
    public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

    // Remote persistence support
    public HubConnection? HubConnection { get; set; }
    public StateDatabase? StateDatabase { get; set; }

    private readonly List<ChatCompletionMessageParam> messages = [];
    private readonly Dictionary<string, string> pendingStateChanges = new();

    public static Session New(string? sessionId = null)
    {
        return new Session
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString()
        };
    }

    public void AddMessages(ICollection<ChatCompletionMessageParam> messages)
    {
        this.messages.AddRange(messages);
    }

    public ICollection<ChatCompletionMessageParam> GetMessages()
    {
        return messages.ToArray();
    }

    // Track state changes for incremental sync
    public void OnStateChanged(string key, string value)
    {
        lock (pendingStateChanges)
        {
            pendingStateChanges[key] = value;
        }
    }

    // Unified Load: Local + Remote
    public async Task Load()
    {
        // Load local messages from file
        LoadLocalMessages();

        // Load remote messages and state if connected
        if (HubConnection != null)
        {
            await LoadFromManager();
        }
    }

    // Unified Save: Local + Remote
    public async Task Save(ICollection<ChatCompletionMessageParam> conversationMessages)
    {
        // Save local messages to file
        SaveLocalMessages(conversationMessages);

        // Send to manager if connected
        if (HubConnection != null)
        {
            await SaveToManager(conversationMessages);
        }
    }

    private void LoadLocalMessages()
    {
        var messagesFileName = GetMessagesFilename(SessionId);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        if (!File.Exists(messagesFilePath))
        {
            return;
        }

        List<ChatCompletionMessageParam>? messages = JsonSerializer.Deserialize<List<ChatCompletionMessageParam>>(File.ReadAllText(messagesFilePath));
        if (messages == null)
        {
            return;
        }

        AddMessages(messages);
    }

    private void SaveLocalMessages(ICollection<ChatCompletionMessageParam> conversationMessages)
    {
        var messagesFileName = GetMessagesFilename(SessionId);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        File.WriteAllText(messagesFilePath, JsonSerializer.Serialize(conversationMessages, serializerOptions));
        this.messages.Clear();
        this.messages.AddRange(conversationMessages);
    }

    private async Task LoadFromManager()
    {
        try
        {
            // Get messages from manager
            var remoteMessagesJson = await HubConnection!.InvokeAsync<string>("GetMessages", SessionId);
            if (!string.IsNullOrEmpty(remoteMessagesJson))
            {
                var remoteMessages = JsonSerializer.Deserialize<List<ChatCompletionMessageParam>>(remoteMessagesJson);
                if (remoteMessages != null)
                {
                    AddMessages(remoteMessages);
                }
            }

            // Get state from manager and populate StateDatabase
            if (StateDatabase != null)
            {
                var allState = await HubConnection!.InvokeAsync<Dictionary<string, string>>("GetAllState", SessionId);
                if (allState != null)
                {
                    foreach (var (key, value) in allState)
                    {
                        StateDatabase.SetState(SessionId, key, value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but continue - remote load failure shouldn't break local operation
            Console.WriteLine($"Failed to load from manager: {ex.Message}");
        }
    }

    private async Task SaveToManager(ICollection<ChatCompletionMessageParam> conversationMessages)
    {
        try
        {
            // Send messages to manager
            var messagesJson = JsonSerializer.Serialize(conversationMessages);
            await HubConnection!.InvokeAsync("AddMessages", SessionId, messagesJson);

            // Send pending state changes
            Dictionary<string, string> changesToSend;
            lock (pendingStateChanges)
            {
                changesToSend = new Dictionary<string, string>(pendingStateChanges);
                pendingStateChanges.Clear();
            }

            foreach (var (key, value) in changesToSend)
            {
                await HubConnection!.InvokeAsync("SyncState", SessionId, key, value);
            }
        }
        catch (Exception ex)
        {
            // Log error but continue - remote save failure shouldn't break local operation
            Console.WriteLine($"Failed to save to manager: {ex.Message}");
        }
    }

    private static string GetMessagesFilename(string id)
    {
        return $"messages-{id}.json";
    }
}
