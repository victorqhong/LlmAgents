using LlmAgents.LlmApi.Content;

namespace LlmAgents.Communication;

public abstract class SessionCommunication
{
    public Action? PreWaitForContent { get; set; }

    public Action? PostWaitForContent { get; set; }

    public Action? PreSendMessage { get; set; }

    public Action? PostSendMessage { get; set; }

    protected abstract Task SendMessageImpl(string message, bool newLine);

    protected abstract Task<IEnumerable<IMessageContent>?> WaitForContentImpl(CancellationToken cancellationToken);
    
    public Task SendMessage(string message, bool newLine)
    {
        PreSendMessage?.Invoke();
        var result = SendMessageImpl(message, newLine);
        PostSendMessage?.Invoke();

        return result;
    }

    public async Task<IEnumerable<IMessageContent>?> WaitForContent(CancellationToken cancellationToken)
    {
        PreWaitForContent?.Invoke();
        var result = await WaitForContentImpl(cancellationToken);
        PostWaitForContent?.Invoke();

        return result;
    }
}
