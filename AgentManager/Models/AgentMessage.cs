
namespace AgentManager.Models;

public class AgentMessage
{
    public required AgentSession Session { get; set; }
    public required string Message { get; set; }
}