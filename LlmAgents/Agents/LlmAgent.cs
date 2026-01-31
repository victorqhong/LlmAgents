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

    public readonly string Id;

    public bool Persistent { get; set; }

    public bool StreamOutput { get; set; }

    public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

    public Action? PreWaitForContent { get; set; }

    public Action? PostReceiveContent { get; set; }

    public Action? PostSendMessage { get; set; }

    public IToolEventBus? ToolEventBus { get; set; }

    public string? SessionId { get; set; }

    public StateDatabase? StateDatabase { get; set; }

    public List<JObject> Messages { get; private set; } = [];

    private readonly List<ILlmAgentWork> tasks = [];

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
        return tasks.SelectMany((work, selector) => work.Messages ?? []).ToList();
    }

    private void AddWorkToTasks(ILlmAgentWork work, ILlmAgentWork? predecessor)
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

    private async Task<T> RunWork<T>(T work, ILlmAgentWork? predecessor, CancellationToken cancellationToken) where T : LlmAgentWork
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

                if (assistantWork.Parser == null || !string.Equals(assistantWork.Parser.FinishReason, "tool_calls"))
                {
                    continue;
                }

                var toolCallsWork = await RunWork(new ToolCalls(this), assistantWork, cancellationToken);

            }
        }, cancellationToken);

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        // while (!cancellationToken.IsCancellationRequested)
        // {
            // PreWaitForContent?.Invoke();
            //
            // var messageContent = await agentCommunication.WaitForContent(cancellationToken);
            // if (cancellationToken.IsCancellationRequested || messageContent == null)
            // {
            //     break;
            // }
            //
            // PostReceiveContent?.Invoke();
            //
            // if (StreamOutput)
            // {
            //     var response = await llmApi.GenerateStreamingCompletion(messageContent, cancellationToken);
            //     if (response == null)
            //     {
            //         continue;
            //     }
            //
            //     await foreach (var chunk in response)
            //     {
            //         await agentCommunication.SendMessage(chunk, false);
            //     }
            //
            //     await agentCommunication.SendMessage(string.Empty, true);
            // }
            // else
            // {
            //     var response = await llmApi.GenerateCompletion(messageContent, cancellationToken);
            //     if (response == null)
            //     {
            //         continue;
            //     }
            //
            //     await agentCommunication.SendMessage(response, true);
            // }
            //
            // PostSendMessage?.Invoke();
            //
            // if (Persistent)
            // {
            //     SaveMessages();
            // }
        // }
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

        messages.Clear();
        messages.AddRange(messages);
    }

    public void SaveMessages()
    {
        var messagesFileName = GetMessagesFilename(Id);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        File.WriteAllText(messagesFilePath, JsonConvert.SerializeObject(Messages));
    }

    private static string GetMessagesFilename(string agentId)
    {
        return $"messages-{agentId}.json";
    }
}
