using Microsoft.Extensions.Logging;
using XmppAgent.Communication;

namespace XmppAgent.Logging
{
    public class XmppLogger : ILogger
    {
        private readonly XmppCommunication xmppCommunication;

        public XmppLogger(XmppCommunication xmppCommunication)
        {
            this.xmppCommunication = xmppCommunication;
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
                xmppCommunication.SendMessage(message);
                if (exception != null)
                {
                    xmppCommunication.SendMessage(exception.Message);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
