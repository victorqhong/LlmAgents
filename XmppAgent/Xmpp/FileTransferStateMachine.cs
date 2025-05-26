namespace XmppAgent.Xmpp;

using LlmAgents.Agents;
using XmppDotNet;

public class FileTransferStateMachine : XmppStateMachine<MessageContentImageUrl>
{
    public FileTransferStateMachine(XmppClient xmppClient)
        : base(xmppClient)
    {

    }

    public override void Reset()
    {
    }
}
