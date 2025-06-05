namespace LlmAgents.LlmApi.Content;

public class MessageContentImageUrl : IMessageContent
{
    public required string MimeType;
    public required string DataBase64;
}
