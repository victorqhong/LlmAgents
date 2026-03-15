
namespace AgentManager.Models;

public class AgentConnection
{
    public required Guid SessionId { get; set; }
    public required string ConnectionId { get; set; }
    public string? IpAddress { get; set; }
}
