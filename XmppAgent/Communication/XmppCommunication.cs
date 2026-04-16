using LlmAgents.Communication;
using LlmAgents.Configuration;
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

    /// <summary>
    /// Controls message batching behavior.
    /// - Negative values: Send entire message in one batch (no batching)
    /// - Zero: Don't send the message at all
    /// - Positive values: Split message into chunks of this size
    /// </summary>
    public int MessageBatchSize { get; set; } = -1;

    /// <summary>
    /// Delay in milliseconds between each batch when MessageBatchSize is positive.
    /// Default is 0 (no delay).
    /// </summary>
    public int MessageBatchDelayMs { get; set; } = 0;

    private readonly IncomingMessageStateMachine incomingMessageStateMachine;

    private readonly FileTransferStateMachine fileTransferStateMachine;

    public XmppCommunication(XmppConfig xmppConfig)
        : this(xmppConfig.XmppUsername, xmppConfig.XmppDomain, xmppConfig.XmppPassword, null, null, null, xmppConfig.XmppTrustHost)
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
            .Subscribe(async state =>
            {
                if (state == SessionState.Binded)
                {
                    await XmppClient.SendPresenceAsync(Show.Chat);
                    Connected = true;
                }
                else if (state == SessionState.Disconnected)
                {
                    Connected = false;
                }
            });

        HandleDiscoInfo();
        HandlePresenceSubscribe();

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

    public Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken = default)
    {
        if (!Connected)
        {
            return Task.FromResult<IEnumerable<IMessageContent>?>(null);
        }

        if (string.IsNullOrEmpty(TargetJid))
        {
            return Task.FromResult<IEnumerable<IMessageContent>?>(null);
        }

        incomingMessageStateMachine.SetIncomingMessageAddress(TargetJid);

        incomingMessageStateMachine.Begin();
        fileTransferStateMachine.Begin();

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

        fileTransferStateMachine.End();
        incomingMessageStateMachine.End();

        if (incomingMessageStateMachine.Result == null)
        {
            return Task.FromResult<IEnumerable<IMessageContent>?>([]);
        }

        var content = new List<IMessageContent>();
        content.Add(incomingMessageStateMachine.Result);

        if (fileTransferStateMachine.Result != null)
        {
            content.AddRange(fileTransferStateMachine.Result);
        }

        return Task.FromResult<IEnumerable<IMessageContent>?>(content);
    }

    public async Task SendMessage(string message, bool newLine)
    {
        if (!Connected)
        {
            return;
        }

        // newLine parameter is part of the interface but not applicable to XMPP
        // (each message is sent as a separate chat message)

        if (MessageBatchSize == 0)
        {
            return;
        }

        if (MessageBatchSize < 0)
        {
            await XmppClient.SendChatMessageAsync(new Jid(TargetJid), message);
            return;
        }

        int batch = 0;
        int remaining = message.Length;
        while (remaining > 0)
        {
            if (remaining >= MessageBatchSize)
            {
                await XmppClient.SendChatMessageAsync(new Jid(TargetJid), message.Substring(batch * MessageBatchSize, MessageBatchSize));
                remaining -= MessageBatchSize;
                batch++;
            }
            else
            {
                await XmppClient.SendChatMessageAsync(new Jid(TargetJid), message.Substring(batch * MessageBatchSize, remaining));
                remaining -= remaining;
            }

            // Apply delay between batches (not after the last batch)
            if (remaining > 0 && MessageBatchDelayMs > 0)
            {
                await Task.Delay(MessageBatchDelayMs);
            }
        }
    }

    public async Task SendComposing()
    {
        if (!Connected || string.IsNullOrEmpty(TargetJid))
        {
            return;
        }

        await SendChatStateAsync(new Jid(TargetJid), "composing");
    }

    public async Task SendActive()
    {
        if (!Connected || string.IsNullOrEmpty(TargetJid))
        {
            return;
        }

        await SendChatStateAsync(new Jid(TargetJid), "active");
    }

    private async Task SendChatStateAsync(Jid target, string state)
    {
        var message = new Message { To = target, Type = MessageType.Chat };
        var stateElement = new XmppXElement(XName.Get(state, "http://jabber.org/protocol/chatstates"));
        message.Add(stateElement);
        await XmppClient.SendMessageAsync(message);
    }

    private void HandleDiscoInfo()
    {
        static XmppXElement CreateFeature(string var)
        {
            var el = new XmppXElement(XName.Get("feature", "http://jabber.org/protocol/disco#info"));
            el.SetAttribute("var", var);

            return el;
        }
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
                resultQuery.Add(CreateFeature("http://jabber.org/protocol/chatstates"));

                var result = new Iq(elFrom, elTo, IqType.Result, elId);
                result.Add(resultQuery);

                Task.Run(async () => await XmppClient.SendIqAsync(result));
            });
    }

    private void HandlePresenceSubscribe()
    {
        XmppClient.XmppXElementReceived
            .Where(el =>
            {
                if (!el.OfType<Presence>()) return false;

                var presence = el.Cast<Presence>();
                if (presence.Type != PresenceType.Subscribe) return false;

                return true;
            })
            .Subscribe(el =>
            {
                var elTo = el.GetAttributeJid("to");
                var elFrom = el.GetAttributeJid("from");
                var response = new Presence
                {
                    From = elTo,
                    To = elFrom,
                    Type = PresenceType.Subscribed
                };
                Task.Run(async () => await XmppClient.SendPresenceAsync(response));

                var subscribeRequest = new Presence
                {
                    From = elTo,
                    To = elFrom,
                    Type = PresenceType.Subscribe
                };
                Task.Run(async () => await XmppClient.SendPresenceAsync(subscribeRequest));
            });
    }
}
