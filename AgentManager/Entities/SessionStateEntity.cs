namespace AgentManager.Entities;

public class SessionStateEntity
{
    public required int Id { get; set; }
    public required string SessionId { get; set; }
    public required string Key { get; set; }
    public required string Value { get; set; }
    public required DateTime UpdatedAt { get; set; }

    public SessionEntity? Session { get; set; }
}