using LlmAgents.CommandLineParser;
using System.CommandLine;

namespace XmppAgent;

internal static class Options
{
    public static readonly Option<string> XmppDomain = new Option<string>(
        name: "--xmppDomain",
        description: "XMPP domain used for agent communication");

    public static readonly Option<string> XmppUsername = new Option<string>(
        name: "--xmppUsername",
        description: "XMPP username used for agent communication");

    public static readonly Option<string> XmppPassword = new Option<string>(
        name: "--xmppPassword",
        description: "XMPP password used for agent communication");

    public static readonly Option<string> XmppTargetJid = new Option<string>(
        name: "--xmppTargetJid",
        description: "The target address the agent should communicate with");

    public static readonly Option<bool> XmppTrustHost = new Option<bool>(
        name: "--xmppTrustHost",
        description: "Whether the XMPP connection should accept untrusted TLS certificates",
        getDefaultValue: () => false);

    public static readonly Option<string?> XmppConfig = new Option<string?>(
        name: "--xmppConfig",
        description: "Path to a JSON file with configuration for XMPP values",
        getDefaultValue: () => Config.GetConfigOptionDefaultValue("xmpp.json", "XMPP_CONFIG"));

    public static readonly Option<string?> AgentsConfig = new Option<string?>(
        name: "--agentsConfig",
        description: "Path to a JSON file with configuration for agents",
        getDefaultValue: () => "agents.json");
}