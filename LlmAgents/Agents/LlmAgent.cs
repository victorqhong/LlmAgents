namespace LlmAgents.Agents;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Agents.Work;
using LlmAgents.Communication;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;

public class LlmAgent
{
    private readonly ILoggerFactory loggerFactory;

    public readonly LlmApiOpenAi llmApi;

    public readonly IAgentCommunication agentCommunication;

    public readonly string Id;

    public readonly SessionCapability SessionCapability;

    public readonly ToolCallCapability ToolCallCapability;

    public bool StreamOutput { get; set; }

    public Action? PreWaitForContent { get; set; }

    public Action? PostReceiveContent { get; set; }

    public Action? PreGetResponse { get; set; }

    public Action? PostSendMessage { get; set; }

    public Action<ChatCompletionUsage>? PostParseUsage { get; set; }

    public Action<LlmAgentWork>? PostRunWork { get; set; }

    public Func<LlmAgent, GetUserInputWork> CreateUserInputWork { get; set; } = agent => new GetUserInputWork(agent);

    public Func<LlmAgent, GetAssistantResponseWork> CreateAssistantResponseWork { get; set; } = agent => new GetAssistantResponseWork(agent.loggerFactory, agent);

    public Func<LlmAgent, ToolCalls> CreateToolCallsWork { get; set; } = agent => new ToolCalls(agent.loggerFactory, agent);

    public LlmAgent(LlmAgentParameters parameters, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication, ILoggerFactory loggerFactory)
        : this(parameters.AgentId, llmApi, agentCommunication, loggerFactory)
    {
        StreamOutput = parameters.StreamOutput;

        SessionCapability.Persistent = parameters.Persistent;
    }

    public LlmAgent(string id, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication, ILoggerFactory loggerFactory)
    {
        Id = id;
        this.llmApi = llmApi;
        this.agentCommunication = agentCommunication;
        this.loggerFactory = loggerFactory;

        SessionCapability = new SessionCapability(loggerFactory, this);
        ToolCallCapability = new ToolCallCapability(this);
    }

    public async Task<T> RunWork<T>(T work, LlmAgentWork? predecessor, CancellationToken cancellationToken) where T : LlmAgentWork
    {
        await work.Run(cancellationToken);

        PostRunWork?.Invoke(work);

        return work;
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var userInputWork = await RunWork(CreateUserInputWork(this), null, cancellationToken);
                var assistantWork = await RunWork(CreateAssistantResponseWork(this), userInputWork, cancellationToken);

                while (assistantWork.Parser?.FinishReason == ChatCompletionChoiceFinishReason.ToolCalls)
                {
                    var toolCallsWork = await RunWork(CreateToolCallsWork(this), assistantWork, cancellationToken);
                    assistantWork = await RunWork(CreateAssistantResponseWork(this), toolCallsWork, cancellationToken);
                }
            }
        }, cancellationToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

public class ToolCallCapability : AgentCapability
{
    private readonly List<ChatCompletionFunctionTool> ToolDefinitions = [];

    private readonly Dictionary<string, Tool> ToolMap = [];

    public ToolCallCapability(LlmAgent agent)
        : base(agent)
    {
    }

    public Action<string, JsonDocument, JsonNode>? ToolCalled { get; set; }

    public IToolEventBus? ToolEventBus { get; set; }

    public List<ChatCompletionFunctionTool> GetToolDefinitions()
    {
        return ToolDefinitions;
    }

    public void AddTool(params Tool[] tools)
    {
        foreach (var tool in tools)
        {
            AddTool(tool);
        }
    }

    public void AddTool(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        ToolDefinitions.Add(tool.Schema);
        ToolMap.Add(tool.Name, tool);
    }

    public async Task<JsonNode?> CallTool(string toolName, JsonDocument arguments, Session session)
    {
        if (!ToolMap.TryGetValue(toolName, out var tool))
        {
            return null;
        }

        var result = await tool.Function(session, arguments);
        ToolEventBus?.PostCallToolEvent(tool, arguments, result);
        ToolCalled?.Invoke(toolName, arguments, result);

        if (session != null)
        {
            await tool.Save(session);
        }

        return result;
    }
}

public class SessionCapability : AgentCapability
{
    public SessionCapability(ILoggerFactory loggerFactory, LlmAgent agent)
        : base(agent)
    {
        Session = Session.Ephemeral(loggerFactory);

        agent.PostRunWork += PostRunWork;
    }

    public bool Persistent { get; set; }

    public bool OutputMessagesOnLoad { get; set; }

    public Session Session { get; private set; }

    public async Task Load(Session session, CancellationToken cancellationToken)
    {
        Session = session;

        if (OutputMessagesOnLoad)
        {
            await OutputMessages();
        }
    }

    public List<ChatCompletionMessageParam> RenderConversation()
    {
        return Session.GetMessages().ToList();
    }

    private async void PostRunWork(LlmAgentWork work)
    {
        if (work.Messages != null)
        {
            Session.AddMessages(work.Messages);
        }

        if (Persistent)
        {
            await Session.Save();
        }
    }

    private async Task OutputMessages()
    {
        foreach (var message in Session.GetMessages())
        {
            if (message is ChatCompletionMessageParamUser userMessage)
            {
                if (userMessage.Content is ChatCompletionMessageParamContentString contentString)
                {
                    await agent.agentCommunication.SendMessage($"User: {contentString.Content}", true);
                }
                else if (userMessage.Content is ChatCompletionMessageParamContentParts contentParts)
                {
                    foreach (var part in contentParts.Content)
                    {
                        if (part is not ChatCompletionContentPartText textPart)
                        {
                            continue;
                        }

                        await agent.agentCommunication.SendMessage($"User: {textPart.Text}", true);
                    }
                }
            }
            else if (message is ChatCompletionMessageParamAssistant assistantMessage && assistantMessage.Content is ChatCompletionMessageParamContentString stringContent && !string.IsNullOrEmpty(stringContent.Content))
            {
                await agent.agentCommunication.SendMessage($"Assistant: {stringContent.Content}", true);
            }
        }
    }
}

public class AgentCapability
{
    protected readonly LlmAgent agent;

    public AgentCapability(LlmAgent agent)
    {
        this.agent = agent;
    }
}
