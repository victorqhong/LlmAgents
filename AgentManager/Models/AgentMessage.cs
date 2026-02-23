
namespace AgentManager.Models;

public class AgentMessage
{
    public required AgentSession Session { get; set; }
    public required string Role { get; set; }
    public string? TextContent { get; set; }
    public string? ImageContent { get; set; }
    public string? ImageContentMimeType { get; set; }
}