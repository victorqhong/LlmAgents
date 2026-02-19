
namespace AgentManager.Entities;

public class SessionEntity
{
    public required Guid Id { get; set; }
    public string? SessionName { get; set; }
    public required string AgentName { get; set; }
    public required string Status { get; set; }
    public required bool Persistent { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public ICollection<LogEntity> Logs { get; set; } = [];
    public ICollection<MessageEntity> Messages { get; set; } = [];
}