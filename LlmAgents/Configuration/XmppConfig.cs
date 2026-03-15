using System.Text.Json.Serialization;

namespace LlmAgents.Configuration;

public class XmppConfig 
{
    [JsonPropertyName("xmppDomain")]
    public required string XmppDomain { get; set; }

    [JsonPropertyName("xmppUsername")]
    public required string XmppUsername { get; set; }

    [JsonPropertyName("xmppPassword")]
    public required string XmppPassword { get; set; }

    [JsonPropertyName("xmppTargetJid")]
    public required string XmppTargetJid { get; set; }

    [JsonPropertyName("xmppTrustHost")]
    public required bool XmppTrustHost { get; set; }

    public bool Valid()
    {
        return !string.IsNullOrEmpty(XmppTargetJid) && !string.IsNullOrEmpty(XmppDomain) && !string.IsNullOrEmpty(XmppUsername) && !string.IsNullOrEmpty(XmppPassword);
    }
}

