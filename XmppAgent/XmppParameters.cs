using Newtonsoft.Json.Linq;
using System.CommandLine;
namespace XmppAgent;

public class XmppParameters
{
    public required string XmppTargetJid;
    public required string XmppDomain;
    public required string XmppUsername;
    public required string XmppPassword;
    public bool XmppTrustHost = false;

    public static XmppParameters? ParseXmppParameters(ParseResult parseResult)
    {
        string? xmppDomain = null;
        string? xmppUsername = null;
        string? xmppPassword = null;
        string? xmppTargetJid = null;
        bool xmppTrustHost = false;

        var xmppConfigValue = parseResult.GetValue(Options.XmppConfig);
        if (!string.IsNullOrEmpty(xmppConfigValue) && File.Exists(xmppConfigValue))
        {
            var xmppConfig = JObject.Parse(File.ReadAllText(xmppConfigValue));
            if (xmppConfig != null)
            {
                xmppDomain = xmppConfig.Value<string>("xmppDomain");
                xmppUsername = xmppConfig.Value<string>("xmppUsername");
                xmppPassword = xmppConfig.Value<string>("xmppPassword");
                xmppTargetJid = xmppConfig.Value<string>("xmppTargetJid");
                xmppTrustHost = xmppConfig.Value<bool>("xmppTrustHost");
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

        return new XmppParameters
        {
            XmppDomain = xmppDomain,
            XmppUsername = xmppUsername,
            XmppPassword = xmppPassword,
            XmppTargetJid = xmppTargetJid,
            XmppTrustHost = xmppTrustHost
        };
    }
}
