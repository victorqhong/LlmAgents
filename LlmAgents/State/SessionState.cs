namespace LlmAgents.State;

public class State
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public required string SessionId { get; set; }
    public DateTime UpdatedAt { get; set; }
}
