using Microsoft.Extensions.Logging;
using XmppAgent.Communication;

namespace XmppAgent.Logging
{
    public class XmppLogger : ILogger
    {
        private readonly XmppCommunication xmppCommunication;
        private readonly string targetJid;

        public XmppLogger(XmppCommunication xmppCommunication, string targetJid)
        {
            this.xmppCommunication = xmppCommunication;
            this.targetJid = targetJid;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                xmppCommunication.SendMessage(targetJid, message);
            }
            catch (Exception e)
            {
                xmppCommunication.SendMessage(targetJid, e.Message);
            }
        }
    }
}
