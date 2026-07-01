namespace LlmAgents.Agents;

using LlmAgents.Agents.Work;
using LlmAgents.LlmApi.Content;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Agents.Capabilities;
using Microsoft.Extensions.Logging;

public class LlmAgent
{
    private readonly ILoggerFactory loggerFactory;

    public readonly LlmApiOpenAi llmApi;

    public readonly string Id;

    public readonly SessionCapability SessionCapability;

    public readonly ToolCallCapability ToolCallCapability;

    public Func<Session, LlmAgentWork, CancellationToken, Task>? PreRunWork { get; set; }

    public Func<Session, LlmAgentWork, CancellationToken, Task>? PostRunWork { get; set; }

    public Action<Session>? PreProcessSession { get; set; }

    public Action<Session>? PostProcessSession { get; set; }

    public Action<LlmAgent, GetAssistantResponseWork>? ConfigureAssistantResponseWork { get; set; }

    public Action<LlmAgent, ToolCalls>? ConfigureToolCallsWork { get; set; }

    public LlmAgent(LlmAgentParameters parameters, LlmApiOpenAi llmApi, ILoggerFactory loggerFactory)
        : this(parameters.AgentId, llmApi, loggerFactory, new StateDatabase(loggerFactory, Path.Join(parameters.StorageDirectory, $"{parameters.AgentId}.db")))
    {
        SessionCapability.Persistent = parameters.Persistent;
    }

    public LlmAgent(string id, LlmApiOpenAi llmApi, ILoggerFactory loggerFactory, StateDatabase stateDatabase)
    {
        Id = id;
        this.llmApi = llmApi;
        this.loggerFactory = loggerFactory;

        SessionCapability = new SessionCapability(stateDatabase, this);
        ToolCallCapability = new ToolCallCapability(loggerFactory, this);
        ToolCallCapability.ToolFactory.Register(stateDatabase);
    }

    public async Task PostInput(SessionCapability.SessionHandle handle, IEnumerable<IMessageContent> input, CancellationToken cancellationToken)
    {
        await SessionCapability.PostInput(handle, input, cancellationToken);
    }

    private async Task<T> RunWork<T>(Session session, T work, CancellationToken cancellationToken) where T : LlmAgentWork
    {
        if (PreRunWork != null)
        {
            await PreRunWork.Invoke(session, work, cancellationToken);
        }

        await work.Run(session, cancellationToken);
        if (work.Messages != null)
        {
            session.AddMessages(work.Messages);
        }

        if (PostRunWork != null)
        {
            await PostRunWork.Invoke(session, work, cancellationToken);
        }

        return work;
    }

    private GetAssistantResponseWork CreateAssistantResponseWork()
    {
        var work = new GetAssistantResponseWork(loggerFactory, this);
        ConfigureAssistantResponseWork?.Invoke(this, work);
        return work;
    }

    private ToolCalls CreateToolCallsWork()
    {
        var work = new ToolCalls(loggerFactory, this);
        ConfigureToolCallsWork?.Invoke(this, work);
        return work;
    }

    private async Task ProcessSession(Session session, CancellationToken cancellationToken)
    {
        PreProcessSession?.Invoke(session);

        var assistantWork = await RunWork(session, CreateAssistantResponseWork(), cancellationToken);
        while (assistantWork.Parser?.FinishReason == ChatCompletionChoiceFinishReason.ToolCalls)
        {
            await RunWork(session, CreateToolCallsWork(), cancellationToken);
            SessionCapability.DrainPendingMessages(session);
            assistantWork = await RunWork(session, CreateAssistantResponseWork(), cancellationToken);
        }

        PostProcessSession?.Invoke(session);
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var session = await SessionCapability.GetUpdatedSession(cancellationToken);
                if (session == null)
                {
                    continue;
                }

                await ProcessSession(session, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }
}
