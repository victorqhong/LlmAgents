
namespace AgentManager.Entities;

public class MessageEntity
{
    public required int Id { get; set; }
    public SessionEntity Session { get; set; }
    public required string Role { get; set; }
    public string? TextContent { get; set; }
    public string? ImageContent { get; set; }
    public string? ImageContentMimeType { get; set; }
}
