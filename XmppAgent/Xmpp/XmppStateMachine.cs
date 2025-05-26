namespace XmppAgent.Xmpp;

using XmppDotNet;

public abstract class XmppStateMachine<T>
{
    protected readonly XmppClient XmppClient;

    public T? Result { get; protected set; }

    public XmppStateMachine(XmppClient xmppClient)
    {
        ArgumentNullException.ThrowIfNull(xmppClient);

        XmppClient = xmppClient;
    }

    public abstract void Reset();
}
