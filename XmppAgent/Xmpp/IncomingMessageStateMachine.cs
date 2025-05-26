namespace XmppAgent.Xmpp;

using System.Reactive.Linq;
using XmppDotNet;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;

public class IncomingMessageStateMachine : XmppStateMachine<string>
{
    private IDisposable? subscriber;

    public string? TargetJid { get; set; }

    public IncomingMessageStateMachine(XmppClient xmppClient)
        : base(xmppClient)
    {
    }

    public override void Reset()
    {
        if (subscriber != null)
        {
            subscriber.Dispose();
        }

        Result = null;

        var fromJid = new Jid(TargetJid);
        subscriber = XmppClient.XmppXElementReceived
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

                return fromJid.EqualsBare(message.From);
            })
            .Subscribe(el =>
            {
                if (Result != null)
                {
                    return;
                }

                Result = el.GetTag("body");
            });
    }
}
