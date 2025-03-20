using Microsoft.Extensions.Logging;
using XmppAgent.Communication;

namespace XmppAgent.Logging
{
    [ProviderAlias("Xmpp")]
    public class XmppLoggerProvider : ILoggerProvider
    {
        private readonly XmppCommunication xmppCommunication;

        public XmppLoggerProvider(XmppCommunication xmppCommunication)
        {
            this.xmppCommunication = xmppCommunication;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new XmppLogger(xmppCommunication);
        }

        public void Dispose()
        {
        }
    }
}
