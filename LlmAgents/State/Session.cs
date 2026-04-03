using System.Text.Json;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.Extensions.Logging;

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

    public static Session Ephemeral(ILoggerFactory loggerFactory)
    {
        var stateDatabase = new StateDatabase(loggerFactory, ":memory:");
        var sessionDatabase = new SessionDatabase(stateDatabase);
        var session = new Session(Guid.NewGuid().ToString(), sessionDatabase)
        {
            StartTime = DateTime.UtcNow,
            LastActive = DateTime.UtcNow,
            Metadata = string.Empty
        };
        sessionDatabase.CreateSession(session);
        return session;
    }

    public readonly SessionDatabase SessionDatabase;

    public readonly string SessionId;

    public Session(string sessionId, SessionDatabase sessionDatabase)
    {
        SessionId = sessionId;
        SessionDatabase = sessionDatabase;
    }

    public DateTime StartTime { get; set; } = DateTime.UnixEpoch;
    public DateTime LastActive { get; set; } = DateTime.UnixEpoch;
    public string Metadata { get; set; } = string.Empty;
    public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

    protected readonly List<ChatCompletionMessageParam> messages = [];

    public virtual Task<string?> GetState(string key)
    {
        return Task.FromResult(SessionDatabase.GetState(SessionId, key));
    }

    public virtual Task SetState(string key, string value)
    {
        SessionDatabase.SetState(SessionId, key, value);
        return Task.CompletedTask;
    }

    public void AddMessages(ICollection<ChatCompletionMessageParam> messages)
    {
        this.messages.AddRange(messages);
    }

    public ICollection<ChatCompletionMessageParam> GetMessages()
    {
        return messages.ToArray();
    }

    public async virtual Task Load()
    {
        await LoadMessages();
        var session = SessionDatabase.GetSession(SessionId);
        if (session != null)
        {
            StartTime = session.StartTime;
            LastActive = session.LastActive;
            Metadata = session.Metadata;
        }
        else
        {
            SessionDatabase.CreateSession(this);
        }
    }

    public async virtual Task Save()
    {
        await SaveMessages();
        SessionDatabase.UpdateSessionTime(SessionId, DateTime.UtcNow);
    }

    protected virtual Task LoadMessages()
    {
        var messages = LoadMessagesFromDisk();
        if (messages != null)
        {
            AddMessages(messages);
        }

        return Task.CompletedTask;
    }

    protected virtual Task SaveMessages()
    {
        SaveMessagesToDisk(messages);
        return Task.CompletedTask;
    }

    private List<ChatCompletionMessageParam>? LoadMessagesFromDisk()
    {
        var messagesFileName = GetMessagesFilename(SessionId);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        if (!File.Exists(messagesFilePath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<List<ChatCompletionMessageParam>>(File.ReadAllText(messagesFilePath));
    }

    private void SaveMessagesToDisk(List<ChatCompletionMessageParam> messages)
    {
        var messagesFileName = GetMessagesFilename(SessionId);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        File.WriteAllText(messagesFilePath, JsonSerializer.Serialize(messages, serializerOptions));
    }

    private static string GetMessagesFilename(string id)
    {
        return $"messages-{id}.json";
    }
}
