namespace AgentManager.Models;

public class AgentLog
{
    public required AgentSession Session { get; set; }
    public DateTime LogTime { get; set; } = DateTime.UtcNow;
    public required string Category { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
}
