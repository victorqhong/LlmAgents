namespace LlmAgents.Configuration;

public class XmppConfig 
{
    public required string XmppDomain { get; set; }
    public required string XmppUsername { get; set; }
    public required string XmppPassword { get; set; }
    public required string XmppTargetJid { get; set; }
    public required bool XmppTrustHost { get; set; }
}

