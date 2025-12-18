using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using System.Reactive.Linq;
using System.Xml.Linq;
using XmppAgent.Xmpp;
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

    public bool Debug { get; set; } = false;

    private readonly IncomingMessageStateMachine incomingMessageStateMachine;

    private readonly FileTransferStateMachine fileTransferStateMachine;

    public XmppCommunication(XmppParameters xmppParameters)
        : this(xmppParameters.XmppUsername, xmppParameters.XmppDomain, xmppParameters.XmppPassword, null, null, null, xmppParameters.XmppTrustHost)
    {
    }

    public XmppCommunication(string username, string domain, string password, string? resource = null, string? host = null, string? port = null, bool trustHost = false)
    {
        XmppClient = new XmppClient(configuration =>
        {
            if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port))
            {
                configuration.UseSocketTransport(new StaticNameResolver(new Uri($"tcp://{host}:{port}")));
            }
            else
            {
                configuration.UseSocketTransport();
            }

            if (trustHost)
            {
                configuration.WithCertificateValidator(new AlwaysAcceptCertificateValidator());
            }

            configuration.AutoReconnect = true;
        })
        {
            Jid = $"{username}@{domain}",
            Resource = !string.IsNullOrEmpty(resource) ? resource : string.Empty,
            Password = password,
        };

        incomingMessageStateMachine = new IncomingMessageStateMachine(XmppClient);
        fileTransferStateMachine = new FileTransferStateMachine(XmppClient);
    }

    public async Task Initialize()
    {
        if (Debug)
        {
            XmppClient.XmppXElementReceived.Subscribe(Console.WriteLine);
        }

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

        HandleDiscoInfo();

        await XmppClient.ConnectAsync();
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

    public async Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        if (!Connected)
        {
            return null;
        }

        if (string.IsNullOrEmpty(TargetJid))
        {
            return null;
        }

        incomingMessageStateMachine.SetIncomingMessageAddress(TargetJid);

        incomingMessageStateMachine.Begin();
        fileTransferStateMachine.Begin();

        var savedPresence = Presence;

        await XmppClient.SendPresenceAsync(Show.Chat);
        while (incomingMessageStateMachine.Result == null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            incomingMessageStateMachine.Run();
            fileTransferStateMachine.Run();

            Thread.Sleep(1000);
        }

        await XmppClient.SendPresenceAsync(savedPresence);

        fileTransferStateMachine.End();
        incomingMessageStateMachine.End();

        if (incomingMessageStateMachine.Result == null)
        {
            return [];
        }

        var content = new List<IMessageContent>();
        content.Add(incomingMessageStateMachine.Result);

        if (fileTransferStateMachine.Result != null)
        {
            content.AddRange(fileTransferStateMachine.Result);
        }

        return content;
    }

    public async Task SendMessage(string message, bool newLine)
    {
        if (!Connected)
        {
            return;
        }

        await XmppClient.SendChatMessageAsync(new Jid(TargetJid), message);
    }

    private void HandleDiscoInfo()
    {
        Func<string, XmppXElement> CreateFeature = var =>
        {
            var el = new XmppXElement(XName.Get("feature", "http://jabber.org/protocol/disco#info"));
            el.SetAttribute("var", var);

            return el;
        };
        XmppClient.XmppXElementReceived
            .Where(el =>
            {
                if (!el.OfType<Iq>())
                {
                    return false;
                }

                var query = el.Element(XName.Get("query", "http://jabber.org/protocol/disco#info"));
                if (query == null)
                {
                    return false;
                }

                return true;
            })
            .Subscribe(el =>
            {
                var elQuery = el.Element(XName.Get("query", "http://jabber.org/protocol/disco#info"));
                if (elQuery == null)
                {
                    return;
                }

                var elTo = el.GetAttributeJid("to");
                var elFrom = el.GetAttributeJid("from");
                var elId = el.GetAttribute("id");

                var elNode = elQuery.Attribute("node");

                var resultQuery = new XmppXElement(XName.Get("query", "http://jabber.org/protocol/disco#info"));

                var resultIdentity = new XmppXElement(XName.Get("identity", "http://jabber.org/protocol/disco#info"))
                    .SetAttribute("category", "client")
                    .SetAttribute("type", "pc")
                    .SetAttribute("name", "LlmAgents");
                resultQuery.Add(resultIdentity);

                resultQuery.Add(CreateFeature("http://jabber.org/protocol/disco#info"));
                resultQuery.Add(CreateFeature("urn:xmpp:jingle:1"));
                resultQuery.Add(CreateFeature("urn:xmpp:jingle:apps:file-transfer:5"));
                resultQuery.Add(CreateFeature("urn:xmpp:jingle:transports:ibb:1"));

                var result = new Iq(elFrom, elTo, IqType.Result, elId);
                result.Add(resultQuery);

                Task.Run(async () => await XmppClient.SendIqAsync(result));
            });
    }
}
