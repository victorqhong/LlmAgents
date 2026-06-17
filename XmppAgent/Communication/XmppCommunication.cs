using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using XmppAgent.Xmpp;

namespace XmppAgent.Communication;

public class XmppCommunication : SessionCommunication
{
    public readonly string TargetJid;

    public readonly XmppTransport XmppTransport;

    private readonly Queue<IMessageContent> pendingMessages = new();

    private readonly SemaphoreSlim messagesAvailable = new(0);

    private readonly Lock syncObject = new();

    public bool WaitingForContent { get; private set; } = false;

    public XmppCommunication(string targetJid, XmppTransport xmppTransport)
    {
        TargetJid = targetJid;
        XmppTransport = xmppTransport;
    }

    public bool TryAcceptIncomingMessage(XmppMessageMetadata metadata, IMessageContent message)
    {
        lock (syncObject)
        {
            if (!WaitingForContent)
            {
                return false;
            }

            if (!string.Equals(metadata.BareJid, TargetJid))
            {
                return false;
            }

            var shouldRelease = pendingMessages.Count == 0;
            pendingMessages.Enqueue(message);
            if (shouldRelease)
            {
                messagesAvailable.Release();
            }

            return true;
        }
    }

    protected override async Task<IEnumerable<IMessageContent>?> WaitForContentImpl(CancellationToken cancellationToken = default)
    {
        lock (syncObject)
        {
            WaitingForContent = true;
        }

        try
        {
            await messagesAvailable.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (syncObject)
            {
                WaitingForContent = false;
            }
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        lock (syncObject)
        {
            var messages = pendingMessages.ToArray();
            pendingMessages.Clear();
            return messages;
        }
    }

    protected override async Task SendMessageImpl(string message, bool newLine)
    {
        await XmppTransport.SendMessage(TargetJid, message, newLine);
    }
}
