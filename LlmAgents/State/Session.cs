using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LlmAgents.State;

public class Session
{
    public required string SessionId;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    public string Metadata { get; set; } = string.Empty;

    public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

    private readonly List<JObject> messages = [];

    public static Session New(string? sessionId = null)
    {
        return new Session
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString()
        };
    }

    public void AddMessages(ICollection<JObject> messages)
    {
        this.messages.AddRange(messages);
    }

    public ICollection<JObject> GetMessages()
    {
        return messages;
    }

    public void Load()
    {
        var messagesFileName = GetMessagesFilename(SessionId);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        if (!File.Exists(messagesFilePath))
        {
            return;
        }

        List<JObject>? messages = JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(messagesFilePath));
        if (messages == null)
        {
            return;
        }

        AddMessages(messages);
    }

    public void Save(ICollection<JObject> messages)
    {
        var messagesFileName = GetMessagesFilename(SessionId);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        File.WriteAllText(messagesFilePath, JsonConvert.SerializeObject(messages));
    }

    private static string GetMessagesFilename(string id)
    {
        return $"messages-{id}.json";
    }
}
