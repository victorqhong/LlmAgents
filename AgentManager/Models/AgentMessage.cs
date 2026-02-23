using System.Text.Json;

namespace AgentManager.Models;

public class AgentMessage
{
    public required AgentSession Session { get; set; }
    public required string Json { get; set; }
    public required string Role { get; set; }
    public string? TextContent { get; set; }
    public string? ImageContent { get; set; }
    public string? ImageContentMimeType { get; set; }

    public static AgentMessage Parse(JsonElement element, AgentSession agentSession)
    {
        var role = element.GetProperty("role").GetString();
        var contentProperty = element.GetProperty("content");

        string? textContent = null;
        string? imageContent = null;
        string? imageContentMimeType = null;
        if (contentProperty.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in contentProperty.EnumerateArray())
            {
                var type = e.GetProperty("type").GetString();
                if (string.Equals(type, "text"))
                {
                    textContent = e.GetProperty("text").GetString()!;
                    break;
                }
                else if (string.Equals(type, "image_url"))
                {
                    var imageUrl = e.GetProperty("image_url");
                    var url = imageUrl.GetProperty("url").GetString();
                    var parts = url.Split(';');
                    string mimeType = parts[0].Split(':', 2)[1];
                    string dataBase64 = parts[1].Split(',', 2)[1];

                    imageContent = dataBase64;
                    imageContentMimeType = mimeType;
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }
        else if (contentProperty.ValueKind == JsonValueKind.String)
        {
            textContent = contentProperty.GetString()!;
        }
        else
        {
            throw new NotImplementedException();
        }

        return new AgentMessage
        {
            Session = agentSession,
            Role = role,
            TextContent = textContent,
            ImageContent = imageContent,
            ImageContentMimeType = imageContentMimeType,
            Json = element.ToString()
        };
    }

    public static AgentMessage Parse(string json, AgentSession agentSession)
    {
        var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement, agentSession);
    }
}
