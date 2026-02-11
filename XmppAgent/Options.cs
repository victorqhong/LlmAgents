using LlmAgents.CommandLineParser;
using System.CommandLine;

namespace XmppAgent;

internal static class Options
{
    public static readonly Option<string> XmppDomain = new("--xmppDomain")
    {
        Description = "XMPP domain used for agent communication"
    };

    public static readonly Option<string> XmppUsername = new("--xmppUsername")
    {
        Description = "XMPP username used for agent communication"
    };

    public static readonly Option<string> XmppPassword = new("--xmppPassword")
    {
        Description = "XMPP password used for agent communication"
    };

    public static readonly Option<string> XmppTargetJid = new("--xmppTargetJid")
    {
        Description = "The target address the agent should communicate with"
    };

    public static readonly Option<bool> XmppTrustHost = new("--xmppTrustHost")
    {
        Description = "Whether the XMPP connection should accept untrusted TLS certificates",
        DefaultValueFactory = result => false
    };

    public static readonly Option<string?> XmppConfig = new("--xmppConfig")
    {
        Description = "Path to a JSON file with configuration for XMPP values",
        DefaultValueFactory = result => Config.GetConfigOptionDefaultValue("xmpp.json", "XMPP_CONFIG")
    };

    public static readonly Option<string?> AgentsConfig = new("--agentsConfig")
    {
        Description = "Path to a JSON file with configuration for agents",
        DefaultValueFactory = result => "agents.json"
    };
}
