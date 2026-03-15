using System.Text.Json;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
namespace LlmAgents.State;

public class Session
{
    public required string SessionId;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    public string Metadata { get; set; } = string.Empty;

    public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

    private readonly List<ChatCompletionMessageParam> messages = [];

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

        List<ChatCompletionMessageParam>? messages = JsonSerializer.Deserialize<List<ChatCompletionMessageParam>>(File.ReadAllText(messagesFilePath));
        if (messages == null)
        {
            return;
        }

        AddMessages(messages);
    }

    public void Save(ICollection<ChatCompletionMessageParam> messages)
    {
        var messagesFileName = GetMessagesFilename(SessionId);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        File.WriteAllText(messagesFilePath, JsonSerializer.Serialize(messages));
    }

    private static string GetMessagesFilename(string id)
    {
        return $"messages-{id}.json";
    }
}
