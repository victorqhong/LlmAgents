namespace XmppAgent.Xmpp;

using LlmAgents.LlmApi.Content;
using System.Reactive.Linq;
using XmppDotNet;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;

public class IncomingMessageStateMachine : XmppStateMachine<MessageContentText>
{
    private IDisposable? subscriber;

    private Jid? incomingMessageAddress;

    public IncomingMessageStateMachine(XmppClient xmppClient)
        : base(xmppClient)
    {
    }

    public override void Begin()
    {
        Result = null;

        if (subscriber == null)
        {
            subscriber = XmppClient.XmppXElementReceived
                .Where(FilterElement)
                .Subscribe(SubscribeElement);
        }
    }

    public override void End()
    {
        subscriber?.Dispose();
        subscriber = null;
    }

    public override void Run()
    {
    }

    public void SetIncomingMessageAddress(string address)
    {
        incomingMessageAddress = new Jid(address);
    }

    private bool FilterElement(XmppXElement el)
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

        if (message.From == null)
        {
            return false;
        }

        return message.From.EqualsBare(incomingMessageAddress);
    }

    private void SubscribeElement(XmppXElement el)
    {
        if (Result != null)
        {
            return;
        }

        var body = el.GetTag("body");
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        Result = new MessageContentText { Text = body };
    }
}
