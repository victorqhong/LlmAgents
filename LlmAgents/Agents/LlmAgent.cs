namespace LlmAgents.Agents;

using LlmAgents.Agents.Work;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

public class LlmAgent
{
    private readonly ILoggerFactory loggerFactory;

    private readonly List<JObject> ToolDefinitions = [];

    private readonly Dictionary<string, Tool> ToolMap = [];

    public readonly LlmApiOpenAi llmApi;

    public readonly IAgentCommunication agentCommunication;

    private readonly List<LlmAgentWork> tasks = [];

    public readonly string Id;

    public bool Persistent { get; set; }

    public bool StreamOutput { get; set; }

    public Action? PreWaitForContent { get; set; }

    public Action? PostReceiveContent { get; set; }

    public Action? PreGetResponse { get; set; }

    public Action? PostSendMessage { get; set; }

    public Action<TokenUsage>? PostParseUsage { get; set; }

    public Action<string, JObject, JToken>? ToolCalled { get; set; }

    public Action<LlmAgentWork>? PostRunWork { get; set; }

    public IToolEventBus? ToolEventBus { get; set; }

    public Session Session { get; private set; }

    public StateDatabase? StateDatabase { get; private set; }

    public Func<LlmAgent, GetUserInputWork> CreateUserInputWork { get; set; } = agent => new GetUserInputWork(agent);

    public Func<LlmAgent, GetAssistantResponseWork> CreateAssistantResponseWork { get; set; } = agent => new GetAssistantResponseWork(agent);

    public Func<LlmAgent, ToolCalls> CreateToolCallsWork { get; set; } = agent => new ToolCalls(agent.loggerFactory, agent);

    public LlmAgent(LlmAgentParameters parameters, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication, ILoggerFactory loggerFactory)
        : this(parameters.AgentId, llmApi, agentCommunication, loggerFactory)
    {
        Persistent = parameters.Persistent;
        StreamOutput = parameters.StreamOutput;
    }

    public LlmAgent(string id, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication, ILoggerFactory loggerFactory)
    {
        Id = id;
        this.llmApi = llmApi;
        this.agentCommunication = agentCommunication;
        this.loggerFactory = loggerFactory;

        Session = Session.New();
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

    public async Task<JToken?> CallTool(string toolName, JObject arguments)
    {
        if (!ToolMap.TryGetValue(toolName, out var tool))
        {
            return null;
        }

        var result = await tool.Function(Session, arguments);
        ToolEventBus?.PostCallToolEvent(tool, arguments, result);
        ToolCalled?.Invoke(toolName, arguments, result);

        if (Session != null && StateDatabase != null)
        {
            tool.Save(Session, StateDatabase);
        }

        return result;
    }

    public IList<JObject> GetToolDefinitions()
    {
        return ToolDefinitions;
    }

    public List<JObject> RenderConversation()
    {
        return tasks.SelectMany((work, selector) =>
        {
            if (work.Messages != null)
            {
                return work.Messages;
            }

            var state = work.GetState(default).ConfigureAwait(false).GetAwaiter().GetResult();
            if (state != null)
            {
                return state;
            }

            return [];
        }).ToList();
    }

    public async Task<T> RunWork<T>(T work, LlmAgentWork? predecessor, CancellationToken cancellationToken) where T : LlmAgentWork
    {
        AddWorkToTasks(work, predecessor);
        await work.Run(cancellationToken);

        PostRunWork?.Invoke(work);

        if (Persistent)
        {
            Session.Save(RenderConversation());
        }

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

                while (assistantWork.Parser != null && string.Equals(assistantWork.Parser.FinishReason, "tool_calls"))
                {
                    var toolCallsWork = await RunWork(CreateToolCallsWork(this), assistantWork, cancellationToken);
                    assistantWork = await RunWork(CreateAssistantResponseWork(this), toolCallsWork, cancellationToken);
                }
            }
        }, cancellationToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public void LoadSession(Session session, StateDatabase stateDatabase)
    {
        Session = session;
        StateDatabase = stateDatabase;
        AddWorkToTasks(new StaticMessages(Session.GetMessages(), this), null);
    }

    private void AddWorkToTasks(LlmAgentWork work, LlmAgentWork? predecessor)
    {
        if (predecessor == null)
        {
            tasks.Add(work);
        }
        else
        {
            var index = tasks.IndexOf(predecessor);
            if (index == -1 || index == tasks.Count - 1)
            {
                tasks.Add(work);
            }
            else
            {
                tasks.Insert(index + 1, work);
            }
        }
    }

}
