
namespace AgentManager.Entities;

public class MessageEntity
{
    public required int Id { get; set; }
    public required SessionEntity Session { get; set; }
    public required string Json { get; set; }
}
