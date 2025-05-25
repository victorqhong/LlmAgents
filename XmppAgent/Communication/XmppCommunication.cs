using LlmAgents.Communication;
using System.Reactive.Linq;
using XmppDotNet;
using XmppDotNet.Extensions.Client.Message;
using XmppDotNet.Extensions.Client.Presence;
using XmppDotNet.Transport;
using XmppDotNet.Transport.Socket;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;

namespace XmppAgent.Communication;

public class XmppCommunication : IAgentCommunication
{
    public XmppClient XmppClient { get; private set; }

    public bool Connected { get; private set; }

    public required string TargetJid { get; set; }

    public Show Presence { get; private set; }

    public XmppCommunication(XmppParameters xmppParameters)
        : this(xmppParameters.xmppUsername, xmppParameters.xmppDomain, xmppParameters.xmppPassword, null, null, null, xmppParameters.xmppTrustHost)
    {
    }

    public XmppCommunication(string username, string domain, string password, string? resource = null, string? host = null, string? port = null, bool trustHost = false)
    {
        XmppClient = new XmppClient(conf =>
        {
            if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port))
            {
                conf.UseSocketTransport(new StaticNameResolver(new Uri($"tcp://{host}:{port}")));
            }
            else
            {
                conf.UseSocketTransport();
            }

            if (trustHost)
            {
                conf.WithCertificateValidator(new AlwaysAcceptCertificateValidator());
            }

            conf.AutoReconnect = true;
        })
        {
            Jid = $"{username}@{domain}",
            Resource = !string.IsNullOrEmpty(resource) ? resource : string.Empty,
            Password = password,
        };
    }

    public async Task Initialize()
    {
        await XmppClient.ConnectAsync();

        XmppClient.StateChanged
            .Subscribe(state =>
            {
                if (state == SessionState.Binded)
                {
                    Connected = true;
                }
                else if (state == SessionState.Disconnected)
                {
                    Connected = false;
                }
            });
    }

    public async Task SendPresence(Show show)
    {
        if (!Connected)
        {
            return;
        }

        await XmppClient.SendPresenceAsync(show);
        Presence = show;
    }

    public async Task<string?> WaitForMessage(CancellationToken cancellationToken = default)
    {
        if (!Connected)
        {
            return null;
        }

        string? body = null;
        using var subscriber = XmppClient.XmppXElementReceived
            .Where(el =>
            {
                if (!el.OfType<Message>())
                {
                    return false;
                }

                var message = el.Cast<Message>();
                if (message.Type != MessageType.Chat)
                {
                    return false;
                }

                var fromJid = new Jid(TargetJid);
                return fromJid.EqualsBare(message.From);
            })
            .Subscribe(el =>
            {
                if (body != null)
                {
                    return;
                }

                body = el.GetTag("body");
            });

        var savedPresence = Presence;

        await XmppClient.SendPresenceAsync(Show.Chat);
        while (string.IsNullOrEmpty(body))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            Thread.Sleep(1000);
        }

        await XmppClient.SendPresenceAsync(savedPresence);

        return body;
    }

    public async Task SendMessage(string message)
    {
        if (!Connected)
        {
            return;
        }

        await XmppClient.SendChatMessageAsync(new Jid(TargetJid), message);
    }
}
