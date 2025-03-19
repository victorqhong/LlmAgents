using Microsoft.Extensions.Logging;
using XmppAgent.Communication;

namespace XmppAgent.Logging
{
    [ProviderAlias("Xmpp")]
    public class XmppLoggerProvider : ILoggerProvider
    {
        private readonly XmppCommunication xmppCommunication;
        private readonly string targetJid;

        public XmppLoggerProvider(XmppCommunication xmppCommunication, string targetJid)
        {
            this.xmppCommunication = xmppCommunication;
            this.targetJid = targetJid;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new XmppLogger(xmppCommunication, targetJid);
        }

        public void Dispose()
        {
        }
    }
}
