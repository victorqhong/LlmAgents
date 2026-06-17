namespace AgentManager.Models;

public class Agent
{
    public required string Id { get; set; }
    public required string Status { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public required bool Persistent { get; set; }
}
