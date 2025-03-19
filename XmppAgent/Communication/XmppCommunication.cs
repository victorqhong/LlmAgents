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

public class XmppCommunication
{
    public XmppClient XmppClient { get; private set; }

    public bool Connected { get; private set; }

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
        })
        {
            Jid = $"{username}@{domain}",
            Resource = !string.IsNullOrEmpty(resource) ? resource : string.Empty,
            Password = password,
        };

        Initialize().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    private async Task Initialize()
    {
        await XmppClient.ConnectAsync();

        XmppClient.StateChanged
            .Where(s => s == SessionState.Binded)
            .Subscribe(v =>
            {
                Connected = true;
            });
    }

    public void SendPresence(Show show)
    {
        XmppClient.SendPresenceAsync(show).ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public string? WaitForMessage(string jid, CancellationToken cancellationToken = default)
    {
        if (!Connected)
        {
            return null;
        }

        var body = string.Empty;
        Action<XmppXElement> onNext = el =>
        {
            body = el.GetTag("body");
        };

        var subscriber = XmppClient.XmppXElementReceived
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

                var fromJid = new Jid(jid);
                return fromJid.EqualsBare(message.From);
            })
            .Subscribe(onNext);

        XmppClient.SendPresenceAsync(Show.Chat).ConfigureAwait(false).GetAwaiter().GetResult();
        while (string.IsNullOrEmpty(body))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            Thread.Sleep(1000);
        }

        subscriber.Dispose();

        return body;
    }

    public void SendMessage(string jid, string message)
    {
        if (!Connected)
        {
            return;
        }

        XmppClient.SendChatMessageAsync(new Jid(jid), message).ConfigureAwait(false).GetAwaiter().GetResult();
    }
}