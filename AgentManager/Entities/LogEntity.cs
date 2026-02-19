
namespace AgentManager.Entities;

public class LogEntity
{
    public required int Id { get; set; }
    public SessionEntity Session { get; set; }
    public required DateTime LogTime { get; set; }
    public required string Category { get; set; }
    public required string Level { get; set; }
    public required string Message { get; set; }
}
