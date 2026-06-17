namespace XmppAgent.Xmpp;

using LlmAgents.LlmApi.Content;
using System.Reactive.Linq;
using XmppDotNet;
using XmppDotNet.Xml;
using XmppDotNet.Xmpp;
using XmppDotNet.Xmpp.Client;

public class IncomingMessagePublisher
{
    public Action<XmppMessageMetadata, IMessageContent>? OnMessageContent { get; set; }

    public IncomingMessagePublisher(XmppClient xmppClient)
    {
        xmppClient.XmppXElementReceived
            .Where(FilterElement)
            .Subscribe(SubscribeElement);
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

        if (string.IsNullOrEmpty(message.Body))
        {
            return false;
        }

        return true;
    }

    private void SubscribeElement(XmppXElement el)
    {
        var message = el.Cast<Message>();
        var metadata = new XmppMessageMetadata(message.From.Bare);
        var result = new MessageContentText { Text = message.Body };
        OnMessageContent?.Invoke(metadata, result);
    }
}
