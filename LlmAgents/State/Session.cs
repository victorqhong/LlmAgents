namespace LlmAgents.State;

using System;

public class Session
{
    public required string SessionId { get; set; }
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActive { get; set; } = DateTime.UtcNow;
    public required string Status { get; set; }
    public string Metadata { get; set; } = string.Empty;

    public static Session New(string? sessionId = null)
    {
        sessionId ??= Guid.NewGuid().ToString();

        return new Session
        {
            SessionId = sessionId,
            Status = "New"
        };
    }
}
