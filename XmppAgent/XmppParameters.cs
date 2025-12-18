using System.CommandLine.Invocation;

namespace XmppAgent;

public class XmppParameters
{
    public required string XmppTargetJid;
    public required string XmppDomain;
    public required string XmppUsername;
    public required string XmppPassword;
    public bool XmppTrustHost = false;

    public static XmppParameters? ParseXmppParameters(InvocationContext invocationContext)
    {
        var xmppDomainValue = invocationContext.ParseResult.GetValueForOption(Options.XmppDomain);
        var xmppUsernameValue = invocationContext.ParseResult.GetValueForOption(Options.XmppUsername);
        var xmppPasswordValue = invocationContext.ParseResult.GetValueForOption(Options.XmppPassword);
        var xmppTargetJidValue = invocationContext.ParseResult.GetValueForOption(Options.XmppTargetJid);
        var xmppTrustHostValue = invocationContext.ParseResult.GetValueForOption(Options.XmppTrustHost);

        if (string.IsNullOrEmpty(xmppDomainValue) || string.IsNullOrEmpty(xmppUsernameValue) || string.IsNullOrEmpty(xmppPasswordValue) || string.IsNullOrEmpty(xmppTargetJidValue))
        {
            Console.Error.WriteLine("xmppDomain, xmppUsername, xmppPassword and/or xmppTargetJid is null or empty.");
            return null;
        }

        return new XmppParameters
        {
            XmppDomain = xmppDomainValue,
            XmppUsername = xmppUsernameValue,
            XmppPassword = xmppPasswordValue,
            XmppTargetJid = xmppTargetJidValue,
            XmppTrustHost = xmppTrustHostValue
        };
    }
}
