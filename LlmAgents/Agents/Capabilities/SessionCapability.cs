using System.Collections.Concurrent;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

namespace LlmAgents.Agents.Capabilities;

public class SessionCapability : AgentCapability
{
    public record SessionMetadata(string SourceType, string SourceId, string? SessionId);

    public readonly record struct SessionHandle(Guid Guid);

    private readonly ConcurrentDictionary<SessionHandle, SessionContext> sessions = new();

    private readonly ConcurrentDictionary<Session, SessionContext> contextTable = new();

    private readonly SemaphoreSlim pendingSignal = new(0);

    private readonly ConcurrentQueue<SessionHandle> pendingSessions = new();

    private class SessionContext
    {
        public readonly Session Session;

        public readonly SessionCommunication SessionCommunication;

        public readonly SessionHandle Handle;

        public readonly SessionMetadata Metadata;

        public readonly List<IMessageContent> PendingMessages = [];

        public readonly object SyncObject = new();

        public bool IsPending;

        public bool IsProcessing;

        public SessionContext(SessionCommunication sessionCommunication, SessionHandle handle, SessionMetadata metadata, Session session)
        {
            Session = session;
            SessionCommunication = sessionCommunication;
            Handle = handle;
            Metadata = metadata;
        }
    }

    public SessionCapability(StateDatabase stateDatabase, LlmAgent agent)
        : base(agent)
    {
        SessionDatabase = new SessionDatabase(stateDatabase);

        agent.PreProcessSession += PreProcessSession;
        agent.PostProcessSession += PostProcessSession;
        agent.PostRunWork += PostRunWork;
    }

    public Func<string, SessionDatabase, CancellationToken, Task<Session>>? SessionFactory { get; set; }

    public bool Persistent { get; set; }

    public SessionDatabase SessionDatabase { get; private set; }

    public async Task<SessionHandle> CreateSession(SessionMetadata inputMetadata, SessionCommunication sessionCommunication, CancellationToken cancellationToken)
    {
        var handle = new SessionHandle
        {
            Guid = Guid.NewGuid()
        };

        var sessionId = inputMetadata.SessionId ?? Guid.NewGuid().ToString();

        Session session;
        if (SessionFactory != null)
        {
            session = await SessionFactory.Invoke(sessionId, SessionDatabase, cancellationToken);
        }
        else
        {
            session = new Session(sessionId, SessionDatabase);
        }

        var sessionContext = new SessionContext(sessionCommunication, handle, inputMetadata, session);

        sessions.TryAdd(handle, sessionContext);
        contextTable.TryAdd(sessionContext.Session, sessionContext);

        return handle;
    }

    public Session GetSession(SessionHandle handle)
    {
        var context = MapSession(handle);
        if (context == null)
        {
            throw new KeyNotFoundException();
        }

        return context.Session;
    }

    public SessionCommunication GetSessionCommunication(Session session)
    {
        if (!contextTable.TryGetValue(session, out var context))
        {
            throw new KeyNotFoundException();
        }

        return context.SessionCommunication;
    }

    public async Task PostInput(SessionHandle handle, IEnumerable<IMessageContent> input, CancellationToken cancellationToken)
    {
        var sessionContext = MapSession(handle);
        if (sessionContext == null)
        {
            throw new KeyNotFoundException();
        }

        var shouldSignal = false;
        lock (sessionContext.SyncObject)
        {
            sessionContext.PendingMessages.AddRange(input);
            if (!sessionContext.IsPending && !sessionContext.IsProcessing)
            {
                sessionContext.IsPending = true;
                pendingSessions.Enqueue(handle);
                shouldSignal = true;
            }
            else
            {
                sessionContext.IsPending = true;
            }
        }

        if (shouldSignal)
        {
            pendingSignal.Release();
        }

        if (Persistent)
        {
            await sessionContext.Session.Save(cancellationToken);
        }
    }

    public async Task<Session?> GetUpdatedSession(CancellationToken cancellationToken)
    {
        await pendingSignal.WaitAsync(cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        SessionContext? context = null;
        while (context == null && !pendingSessions.IsEmpty)
        {
            if (!pendingSessions.TryDequeue(out var handle))
            {
                continue;
            }

            if (!sessions.TryGetValue(handle, out context))
            {
                continue;
            }
        }

        if (context == null)
        {
            return null;
        }

        lock (context.SyncObject)
        {
            if (context.IsProcessing)
            {
                return null;
            }

            var content = context.PendingMessages.ToArray();
            context.PendingMessages.Clear();
            context.Session.AddMessages([GetMessage(content)]);
        }

        return context.Session;
    }

    private SessionContext? MapSession(SessionHandle handle)
    {
        if (!sessions.TryGetValue(handle, out var sessionContext))
        {
            return null;
        }
        
        return sessionContext;
    }

    private void PostProcessSession(Session session)
    {
        if (!contextTable.TryGetValue(session, out var context))
        {
            return;
        }

        lock (context.SyncObject)
        {
            context.IsProcessing = false;
            if (context.PendingMessages.Count == 0)
            {
                context.IsPending = false;
            }
            else
            {
                pendingSessions.Enqueue(context.Handle);
                pendingSignal.Release();
            }
        }
    }

    private void PreProcessSession(Session session)
    {
        if (!contextTable.TryGetValue(session, out var context))
        {
            return;
        }

        lock (context.SyncObject)
        {
            context.IsProcessing = true;
        }
    }

    private async Task PostRunWork(Session session, LlmAgentWork work, CancellationToken cancellationToken)
    {
        if (Persistent)
        {
            if (!contextTable.TryGetValue(session, out var context))
            {
                throw new KeyNotFoundException();
            }

            await context.Session.Save(cancellationToken);
        }
    }

    private static ChatCompletionMessageParamUser GetMessage(IEnumerable<IMessageContent> messageContents)
    {
        ArgumentNullException.ThrowIfNull(messageContents);

        var content = new List<IChatCompletionContentPart>();

        foreach (var messageContent in messageContents)
        {
            if (messageContent is MessageContentText userMessage)
            {
                content.Add(new ChatCompletionContentPartText
                {
                    Type = "text",
                    Text = userMessage.Text
                });

            }
            else if (messageContent is MessageContentImageUrl imageUrl)
            {
                var url = string.Format("data:{0};base64,{1}", imageUrl.MimeType, imageUrl.DataBase64);

                content.Add(new ChatCompletionContentPartImage
                {
                    Type = "image_url",
                    ImageUrl = new ChatCompletionContentPartImageUrl { Url = url }
                });
            }
        }

        return new ChatCompletionMessageParamUser
        {
            Content = new ChatCompletionMessageParamContentParts { Content = content }
        };
    }
}
