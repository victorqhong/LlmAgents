namespace LlmAgents.Agents;

using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.Content;
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

    private readonly List<AgentWork> tasks = [];

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

    private async Task GetUserInput()
    {
        var messageContent = await agentCommunication.WaitForContent();
        if (messageContent == null)
        {
            return;
        }

        foreach (var content in messageContent)
        {
            if (content is not MessageContentText textContent)
            {
                continue;
            }

            await agentCommunication.SendMessage($"User: {textContent.Text}", true);
        }

        Messages.Add(LlmApiOpenAi.GetMessage(messageContent));

        _ = Task.Run(() => ProcessUserInput(Messages));
    }

    private async Task ProcessUserInput(List<JObject> messages)
    {
        // var maxCompletionTokens = MaxCompletionTokens != null ? MaxCompletionTokens.Value : ContextSize - UsageTotalTokens;
        // if (UsageTotalTokens > ContextSize * 0.75)
        // {
        //     Log.LogWarning("Total usage tokens ({UsageTotalTokens}) exceed target context size ({ContextSize}). Pruning context.", UsageTotalTokens, ContextSize * 0.75);
        //     //await PruneContext(Messages.Count - 1);
        // }

        var parser = await llmApi.GetStreamingCompletion(messages);
        if (parser == null)
        {
            return;
        }

        if (parser.StreamingCompletion == null)
        {
            return;
        }

        await agentCommunication.SendMessage("Assistant: ", true);
        await foreach (var chunk in parser.StreamingCompletion)
        {
            await agentCommunication.SendMessage(chunk, false);
        }

        await agentCommunication.SendMessage(string.Empty, true);

        messages.AddRange(parser.Messages);

        if (parser.FinishReason?.Equals("tool_calls") ?? false)
        {
            await ProcessToolCalls(messages);
        }
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
                await agentCommunication.SendMessage($"Calling tool '{name}' with arguments '{arguments}'", true);
                var toolResult = await CallTool(name, JObject.Parse(arguments));
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

        await ProcessUserInput(messages);
    }

    public async Task CreateUserInputWork(CancellationToken cancellationToken)
    {
        // await agentCommunication.SendMessage($"create user input work", true);
        var userInputWork = new GetUserInputWork(this);
        tasks.Add(userInputWork);
        await userInputWork.StartAsync(cancellationToken).ConfigureAwait(false);
            // .ContinueWith(async t => await CreateUserInputWork(cancellationToken), cancellationToken);;
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        _ = CreateUserInputWork(cancellationToken);
        // var userInputWork = new GetUserInputWork(this);
        // tasks.Add(userInputWork);
        // _ = userInputWork.StartAsync(cancellationToken);
        //
        while (!cancellationToken.IsCancellationRequested)
        {

            // if (!tasks.TryPeek(out var work))
            // {
            //     var task = new Task(async () => { await GetUserInput(); });
            //     tasks.Enqueue(task);
            //     // await Task.Delay(1000, cancellationToken);
            //     continue;
            // }
            //
            // if (work.Status == TaskStatus.Created)
            // {
            //     work.Start();
            // }
            // else if (work.Status == TaskStatus.RanToCompletion)
            // {
            //     _ = tasks.Dequeue();
            // }
            // else if (work.Status == TaskStatus.Running)
            // {
            // }
            // else
            // {
            // }

            await Task.Delay(1000, cancellationToken);

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
        }
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

    internal abstract class AgentWork(string operationName)
    {
        public readonly string OperationName = operationName;

        public abstract Task<ICollection<JObject>> Work(CancellationToken ct);

        public abstract Task OnCompleted(ICollection<JObject> messages, CancellationToken ct);

        public abstract Task<ICollection<JObject>?> GetState(CancellationToken ct);

        public ICollection<JObject>? Result { get; private set; }

        public bool Completed { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Result = await Work(cancellationToken).ConfigureAwait(false);
            Completed = true;
            await OnCompleted(Result, cancellationToken).ConfigureAwait(false);
        }
    }

    internal class GetUserInputWork : AgentWork
    {
        private readonly LlmAgent agent;

        public GetUserInputWork(LlmAgent agent)
            : base("user_input")
        {
            this.agent = agent;
        }

        public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
        {
            return Task.FromResult<ICollection<JObject>?>(null);
        }

        public override async Task OnCompleted(ICollection<JObject> messages, CancellationToken ct)
        {
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

        public override async Task<ICollection<JObject>> Work(CancellationToken ct)
        {
           var messageContent = await agent.agentCommunication.WaitForContent(ct); 
           if (messageContent == null)
           {
               return [];
           }

           return [LlmApiOpenAi.GetMessage(messageContent)];

        }
    }
}

