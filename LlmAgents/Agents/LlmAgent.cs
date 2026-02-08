namespace LlmAgents.Agents;

using LlmAgents.Agents.Work;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class LlmAgent
{
    private readonly List<JObject> ToolDefinitions = [];

    private readonly Dictionary<string, Tool> ToolMap = [];

    public readonly LlmApiOpenAi llmApi;

    public readonly IAgentCommunication agentCommunication;

    private readonly List<LlmAgentWork> tasks = [];

    public readonly string Id;

    public bool Persistent { get; set; }

    public bool StreamOutput { get; set; }

    public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

    public Action? PreWaitForContent { get; set; }

    public Action? PostReceiveContent { get; set; }

    public Action? PostSendMessage { get; set; }

    public Action<TokenUsage>? PostParseUsage { get; set; }

    public IToolEventBus? ToolEventBus { get; set; }

    public string? SessionId { get; set; }

    public StateDatabase? StateDatabase { get; set; }

    public LlmAgent(LlmAgentParameters parameters, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication)
        : this(parameters.AgentId, llmApi, agentCommunication)
    {
        Persistent = parameters.Persistent;
        PersistentMessagesPath = parameters.StorageDirectory;
        StreamOutput = parameters.StreamOutput;
    }

    public LlmAgent(string id, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication)
    {
        Id = id;
        this.llmApi = llmApi;
        this.agentCommunication = agentCommunication;
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

        var result = await tool.Function(arguments);
        ToolEventBus?.PostCallToolEvent(tool, arguments, result);

        if (!string.IsNullOrEmpty(SessionId) && StateDatabase != null)
        {
            tool.Save(SessionId, StateDatabase);
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

        return work;
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var userInputWork = await RunWork(new GetUserInputWork(this), null, cancellationToken);
                var assistantWork = await RunWork(new GetAssistantResponseWork(this), userInputWork, cancellationToken);

                while (assistantWork.Parser != null && string.Equals(assistantWork.Parser.FinishReason, "tool_calls"))
                {
                    var toolCallsWork = await RunWork(new ToolCalls(this), assistantWork, cancellationToken);
                    assistantWork = await RunWork(new GetAssistantResponseWork(this), toolCallsWork, cancellationToken);

                    if (Persistent)
                    {
                        SaveMessages();
                    }
                }

            }
        }, cancellationToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public void AddMessages(ICollection<JObject> messages)
    {
        AddWorkToTasks(new StaticMessages(messages, this), null);
    }

    public void LoadMessages()
    {
        var messagesFileName = GetMessagesFilename(Id);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        if (!File.Exists(messagesFilePath))
        {
            return;
        }

        List<JObject>? messages = JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(messagesFilePath));
        if (messages == null)
        {
            return;
        }

        AddMessages(messages);
    }

    public void SaveMessages()
    {
        var messagesFileName = GetMessagesFilename(Id);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        File.WriteAllText(messagesFilePath, JsonConvert.SerializeObject(RenderConversation()));
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

    private static string GetMessagesFilename(string agentId)
    {
        return $"messages-{agentId}.json";
    }
}
