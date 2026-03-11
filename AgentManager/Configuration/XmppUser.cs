using System.Text.Json.Serialization;

namespace AgentManager.Configuration;

public class XmppUser
{
    [JsonPropertyName("jid")]
    public required string Jid { get; set; }

    [JsonPropertyName("password")]
    public required string Password { get; set; }
}
