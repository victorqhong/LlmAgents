namespace LlmAgents.Agents;

using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.Content;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;

public class LlmAgent
{
    public readonly List<Tool> Tools = [];

    public readonly LlmApiOpenAi llmApi;

    public readonly IAgentCommunication agentCommunication;

    public readonly string Id;

    public bool Persistent { get; set; }

    public bool StreamOutput { get; set; }

    public string PersistentMessagesPath { get; set; } = Environment.CurrentDirectory;

    public Action? PreWaitForContent { get; set; }

    public Action? PostSendMessage { get; set; }

    public IToolEventBus? ToolEventBus { get; set; }

    public string? SessionId { get; set; }

    public List<JObject> Messages { get; private set; } = [];

    public StateDatabase? StateDatabase { get; set; }

    public string ToolChoice { get; set; } = "auto";

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

        Tools.Add(tool);
    }

    public Tool? FindTool(string name)
    {
        return Tools.Find(tool => string.Equals(name, tool.Name));
    }

    public async Task<JToken?> CallTool(string toolName, JObject arguments)
    {
        var tool = FindTool(toolName);
        if (tool == null)
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

    public async Task Run(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            PreWaitForContent?.Invoke();

            var messageContent = await agentCommunication.WaitForContent(cancellationToken);
            if (cancellationToken.IsCancellationRequested || messageContent == null)
            {
                break;
            }

            await SendMessage(messageContent, Tools, ToolChoice, cancellationToken);

            PostSendMessage?.Invoke();

            if (Persistent)
            {
                SaveMessages();
            }
        }
    }

    public async Task SendMessage(IEnumerable<IMessageContent> messageContents, List<Tool>? tools = null, string? toolChoice = null, CancellationToken cancellationToken = default)
    {
        var message = LlmApiOpenAi.GetMessage(messageContents);
        Messages.Add(message);

        if (StreamOutput)
        {
            var response = await llmApi.GenerateStreamingCompletion(Messages, tools, toolChoice, cancellationToken);
            if (response == null)
            {
                return;
            }

            await foreach (var chunk in response)
            {
                await agentCommunication.SendMessage(chunk, false);
            }

            await agentCommunication.SendMessage(string.Empty, true);

            if (string.Equals(llmApi.FinishReason, "tool_calls"))
            {
                await llmApi.ProcessToolCalls(Messages, tools);
                var toolCompletion = await llmApi.GenerateStreamingCompletion(Messages, [], "auto", cancellationToken);
                if (toolCompletion != null)
                {
                    await foreach (var chunk in toolCompletion)
                    {
                        await agentCommunication.SendMessage(chunk, false);
                    }

                    await agentCommunication.SendMessage(string.Empty, true);
                }
            }
        }
        else
        {
            var response = await llmApi.GenerateCompletion(Messages, tools, toolChoice, cancellationToken);
            if (response == null)
            {
                return;
            }

            await agentCommunication.SendMessage(response, true);
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

        Messages.Clear();
        Messages.AddRange(messages);
    }

    public void SaveMessages()
    {
        var messagesFileName = GetMessagesFilename(Id);
        var messagesFilePath = Path.GetFullPath(Path.Combine(PersistentMessagesPath, messagesFileName));

        File.WriteAllText(messagesFilePath, JsonConvert.SerializeObject(Messages));
    }

    public static async Task<LlmAgent> CreateAgent(
        ILoggerFactory loggerFactory, IAgentCommunication agentCommunication,
        string apiEndpoint, string apiKey, string apiModel, int contextSize, int maxCompletionTokens,
        string agentId, string workingDirectory, string storageDirectory, bool persistent = false, string? systemPrompt = null, string? sessionId = null,
        string? toolsFilePath = null, string? toolServerAddress = null, int toolServerPort = 5000)
    {
        var llmApi = new LlmApiOpenAi(loggerFactory, apiEndpoint, apiKey, apiModel)
        {
            ContextSize = contextSize,
            MaxCompletionTokens = maxCompletionTokens
        };

        var agent = new LlmAgent(agentId, llmApi, agentCommunication)
        {
            Persistent = persistent,
            PersistentMessagesPath = storageDirectory,
            SessionId = sessionId
        };

        if (!Path.Exists(workingDirectory))
        {
            Directory.CreateDirectory(workingDirectory);
        }

        if (!Path.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }

        var stateDatabase = new StateDatabase(loggerFactory, Path.Join(storageDirectory, $"{agentId}.db"));
        agent.StateDatabase = stateDatabase;

        Session? session = null;
        if (!string.IsNullOrEmpty(sessionId))
        {
            session = stateDatabase.GetSession(sessionId);
            if (session == null)
            {
                session = new Session
                {
                    SessionId = sessionId,
                    Status = "New"
                };

                stateDatabase.CreateSession(session);
            }
        }

        Tool[]? tools = null;

        if (tools == null && !string.IsNullOrEmpty(toolsFilePath) && File.Exists(toolsFilePath))
        {
            var toolEventBus = new ToolEventBus();
            var toolsFile = JObject.Parse(File.ReadAllText(toolsFilePath));
            var toolFactory = new ToolFactory(loggerFactory, toolsFile);

            toolFactory.Register(agentCommunication);
            toolFactory.Register(loggerFactory);
            toolFactory.Register<ILlmApiMessageProvider>(llmApi);
            toolFactory.Register<IToolEventBus>(toolEventBus);
            toolFactory.Register(stateDatabase);

            toolFactory.AddParameter("basePath", workingDirectory);
            toolFactory.AddParameter("storageDirectory", storageDirectory);

            tools = toolFactory.Load(session, stateDatabase);

            agent.ToolEventBus = toolEventBus;
        }

        if (tools != null)
        {
            agent.AddTool(tools);
        }

        if (persistent)
        {
            agent.LoadMessages();
        }

        if (agent.Messages.Count == 0 && !string.IsNullOrEmpty(systemPrompt))
        {
            agent.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));
        }

        return agent;
    }

    private static string GetMessagesFilename(string agentId)
    {
        return $"messages-{agentId}.json";
    }
}
