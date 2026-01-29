namespace LlmAgents.Agents;

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

    private readonly List<IAgentWork> tasks = [];

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

    public IReadOnlyList<JObject> GetToolDefinitions()
    {
        return ToolDefinitions;
    }

    public List<JObject> RenderConversation()
    {
        return tasks.SelectMany((work, selector) => work.Messages ?? []).ToList();
    }

    internal async Task<IAgentWork> CreateUserInputWork(IAgentWork? predecessor, CancellationToken cancellationToken)
    {
        var userInputWork = new GetUserInputWork(this);

        if (predecessor == null)
        {
            tasks.Add(userInputWork);
        }
        else
        {
            var index = tasks.IndexOf(predecessor);
            if (index == -1 || index == tasks.Count - 1)
            {
                tasks.Add(userInputWork);
            }
            else
            {
                tasks.Insert(index + 1, userInputWork);
            }
        }

        await userInputWork.StartAsync(cancellationToken);

        return userInputWork;
    }

    internal async Task<GetAssistantResponseWork> CreateAssistantResponseWork(IAgentWork? predecessor, CancellationToken cancellationToken)
    {
        var assistantResponseWork = new GetAssistantResponseWork(this);
        if (predecessor == null)
        {
            tasks.Add(assistantResponseWork);
        }
        else
        {
            var index = tasks.IndexOf(predecessor);
            if (index == -1 || index == tasks.Count - 1)
            {
                tasks.Add(assistantResponseWork);
            }
            else
            {
                tasks.Insert(index + 1, assistantResponseWork);
            }
        }

        await assistantResponseWork.StartAsync(cancellationToken);

        return assistantResponseWork;
    }

    internal async Task<IAgentWork> CreateToolCallsWork(IAgentWork? predecessor, CancellationToken cancellationToken)
    {
        var toolCallsWork = new ToolCallWork(this);
        if (predecessor == null)
        {
            tasks.Add(toolCallsWork);
        }
        else
        {
            var index = tasks.IndexOf(predecessor);
            if (index == -1 || index == tasks.Count - 1)
            {
                tasks.Add(toolCallsWork);
            }
            else
            {
                tasks.Insert(index + 1, toolCallsWork);
            }
        }

        await toolCallsWork.StartAsync(cancellationToken);

        return toolCallsWork;
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var userInputWork = await CreateUserInputWork(null, cancellationToken);
                var assistantWork = await CreateAssistantResponseWork(userInputWork, cancellationToken);

                if (assistantWork.WorkResult == null || !string.Equals(assistantWork.WorkResult.FinishReason, "tool_calls"))
                {
                    continue;
                }

                var toolCallsWork = await CreateToolCallsWork(assistantWork, cancellationToken);

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

    internal interface IAgentWork
    {
        Task<ICollection<JObject>?> GetState(CancellationToken cancellationToken);
        ICollection<JObject>? Messages { get; }
    }

    internal abstract class AgentWork<T> : IAgentWork
    {
        public abstract Task<T?> Work(CancellationToken ct);

        public abstract Task OnCompleted(T? result, CancellationToken ct);

        public abstract Task<ICollection<JObject>?> GetState(CancellationToken ct);

        public abstract ICollection<JObject>? Messages { get; protected set; }

        public T? WorkResult { get; private set; }

        public readonly LlmAgent agent;

        public AgentWork(LlmAgent agent)
        {
            this.agent = agent;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var result = await Work(cancellationToken);
            WorkResult = result;
            await OnCompleted(result, cancellationToken);
        }
    }

    internal class GetUserInputWork : AgentWork<ICollection<JObject>>
    {
        public GetUserInputWork(LlmAgent agent)
            : base(agent)
        {
        }

        public override ICollection<JObject>? Messages { get; protected set; }

        public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
        {
            return Task.FromResult<ICollection<JObject>?>(null);
        }

        public override async Task OnCompleted(ICollection<JObject>? messages, CancellationToken ct)
        {
            if (messages == null)
            {
                return;
            }

            Messages = messages;

            foreach (var message in messages)
            {
                var content = message.Value<JArray>("content");
                if (content == null) continue;

                foreach (var c in content)
                {
                    var type = c.Value<string>("type");
                    if (!string.Equals(type, "text"))
                    {
                        continue;
                    }

                    var text = c.Value<string>("text");

                    await agent.agentCommunication.SendMessage($"User: {text}", true);
                }
            }
        }

        public override async Task<ICollection<JObject>?> Work(CancellationToken ct)
        {
           var messageContent = await agent.agentCommunication.WaitForContent(ct); 
           if (messageContent == null)
           {
               return null;
           }

           return [LlmApiOpenAi.GetMessage(messageContent)];

        }
    }

    internal class GetAssistantResponseWork : AgentWork<LlmApiOpenAiStreamingCompletionParser>
    {
        public GetAssistantResponseWork(LlmAgent agent)
            : base(agent)
        {
        }

        public override ICollection<JObject>? Messages { get; protected set; }

        public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
        {
            return Task.FromResult<ICollection<JObject>?>(null);
        }

        public override async Task OnCompleted(LlmApiOpenAiStreamingCompletionParser? result, CancellationToken ct)
        {
            if (result == null || result.StreamingCompletion == null)
            {
                return;
            }

            await agent.agentCommunication.SendMessage("Assistant: ", true);
            await foreach (var chunk in result.StreamingCompletion)
            {
                await agent.agentCommunication.SendMessage(chunk, false);
            }

            await agent.agentCommunication.SendMessage(string.Empty, true);

            Messages = result.Messages;
        }

        public override async Task<LlmApiOpenAiStreamingCompletionParser?> Work(CancellationToken ct)
        {
            var conversation = agent.RenderConversation();
            var parser = await agent.llmApi.GetStreamingCompletion(conversation, agent.ToolDefinitions, "auto", cancellationToken: ct);
            if (parser == null)
            {
                return null;
            }

            return parser;
        }
    }

    internal class ToolCallWork : AgentWork<LlmApiOpenAiStreamingCompletionParser>
    {
        public ToolCallWork(LlmAgent agent)
            : base(agent)
        {
        }

        public override ICollection<JObject>? Messages { get; protected set; }

        public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
        {
            return Task.FromResult<ICollection<JObject>?>(null);
        }

        public override async Task OnCompleted(LlmApiOpenAiStreamingCompletionParser? result, CancellationToken ct)
        {
            if (result == null || result.StreamingCompletion == null)
            {
                return;
            }

            await agent.agentCommunication.SendMessage("Assistant: ", true);
            await foreach (var chunk in result.StreamingCompletion)
            {
                await agent.agentCommunication.SendMessage(chunk, false);
            }

            await agent.agentCommunication.SendMessage(string.Empty, true);

            Messages = result.Messages;
        }

        public override async Task<LlmApiOpenAiStreamingCompletionParser?> Work(CancellationToken ct)
        {
            var conversation = agent.RenderConversation();
            await ProcessToolCalls(conversation);

            var parser = await agent.llmApi.GetStreamingCompletion(conversation, agent.ToolDefinitions, "auto", cancellationToken: ct);
            if (parser == null)
            {
                return null;
            }

            return parser;
        }

        private async Task ProcessToolCalls(List<JObject> messages)
        {
            var toolCalls = messages[^1].Value<JArray>("tool_calls");
            if (toolCalls == null)
            {
                return;
            }

            foreach (JObject toolCall in toolCalls.Cast<JObject>())
            {
                var id = toolCall.Value<string>("id");

                var function = toolCall.Value<JObject>("function");
                if (function == null)
                {
                    messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        content = $"Invalid tool call: tool call does not contain 'function' property"
                    }));

                    continue;
                }

                var name = function.Value<string>("name");
                if (string.IsNullOrEmpty(name))
                {
                    messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        name,
                        content = $"Invalid tool call: tool call does not contain 'name' property"
                    }));

                    continue;
                }

                var arguments = function.Value<string>("arguments");
                if (arguments == null)
                {
                    messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        name,
                        content = $"Invalid tool call: tool call does not contain 'arguments' property"
                    }));

                    continue;
                }

                string toolContent;
                try
                {
                    // await agentCommunication.SendMessage($"Calling tool '{name}' with arguments '{arguments}'", true);
                    var toolResult = await agent.CallTool(name, JObject.Parse(arguments));
                    if (toolResult == null)
                    {
                        messages.Add(JObject.FromObject(new
                        {
                            role = "tool",
                            tool_call_id = id,
                            name,
                            content = $"Invalid tool call: tool {name} could not be found"
                        }));

                        continue;
                    }

                    toolContent = JsonConvert.SerializeObject(toolResult);
                }
                catch (Exception ex)
                {
                    toolContent = $"Got exception: {ex.Message}";
                }

                messages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = toolContent
                }));
            }
        }
    }
}

