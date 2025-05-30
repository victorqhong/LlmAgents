namespace LlmAgents.Agents;

public class MessageContentImageUrl : IMessageContent
{
    public required string MimeType;
    public required string DataBase64;
}
