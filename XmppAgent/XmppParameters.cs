using System.CommandLine;
using System.Text.Json;
using LlmAgents.Configuration;
namespace XmppAgent;

public static class XmppParameterParser
{
    public static XmppConfig? ParseXmppParameters(ParseResult parseResult)
    {
        string? xmppDomain = null;
        string? xmppUsername = null;
        string? xmppPassword = null;
        string? xmppTargetJid = null;
        bool xmppTrustHost = false;

        var xmppConfigValue = parseResult.GetValue(Options.XmppConfig);
        if (!string.IsNullOrEmpty(xmppConfigValue) && File.Exists(xmppConfigValue))
        {
            var xmppConfig = JsonSerializer.Deserialize<XmppConfig>(File.ReadAllText(xmppConfigValue));
            if (xmppConfig != null)
            {
                xmppDomain = xmppConfig.XmppDomain;
                xmppUsername = xmppConfig.XmppUsername;
                xmppPassword = xmppConfig.XmppPassword;
                xmppTargetJid = xmppConfig.XmppTargetJid;
                xmppTrustHost = xmppConfig.XmppTrustHost;
            }
        }
        else
        {
            xmppDomain = parseResult.GetValue(Options.XmppDomain);
            xmppUsername = parseResult.GetValue(Options.XmppUsername);
            xmppPassword = parseResult.GetValue(Options.XmppPassword);
            xmppTargetJid = parseResult.GetValue(Options.XmppTargetJid);
            xmppTrustHost = parseResult.GetValue(Options.XmppTrustHost);
        }

        if (string.IsNullOrEmpty(xmppDomain) || string.IsNullOrEmpty(xmppUsername) || string.IsNullOrEmpty(xmppPassword) || string.IsNullOrEmpty(xmppTargetJid))
        {
            Console.Error.WriteLine("xmppDomain, xmppUsername, xmppPassword and/or xmppTargetJid is null or empty.");
            return null;
        }

        return new XmppConfig
        {
            XmppDomain = xmppDomain,
            XmppUsername = xmppUsername,
            XmppPassword = xmppPassword,
            XmppTargetJid = xmppTargetJid,
            XmppTrustHost = xmppTrustHost
        };
    }
}
