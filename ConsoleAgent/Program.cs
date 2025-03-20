using LlmAgents;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.Todo;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.InteropServices;

var apiEndpoint = "";
var apiKey = "";
var model = "gpt-4o";
var persistent = false;

var environmentVariableTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;
var credentialsFile = Environment.GetEnvironmentVariable("LLM_CREDENTIALS_FILE", environmentVariableTarget);
if (File.Exists(credentialsFile))
{
    var json = JObject.Parse(File.ReadAllText(credentialsFile));
    apiEndpoint = $"{json.Value<string>("AZURE_OPENAI_ENDPOINT")}/openai/deployments/{model}/chat/completions?api-version=2024-08-01-preview";
    apiKey = json.Value<string>("AZURE_OPENAI_API_KEY");
}

if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey))
{
    Log.LogCritical("apiEndpoint or apiKey is null or empty.");
    return;
}

var consoleCommunication = new ConsoleCommunication();

var basePath = Environment.CurrentDirectory;
var restrictToBasePath = true;

var todoDatabase = new TodoDatabase(LoggerFactory, Path.Join(basePath, "todo.db"));

var shellTool = new Shell(LoggerFactory, basePath);
var fileReadTool = new FileRead(basePath, restrictToBasePath);
var fileWriteTool = new FileWrite(basePath, restrictToBasePath);
var fileListTool = new FileList(basePath, restrictToBasePath);
var sqliteFileRun = new SqliteFileRun(basePath, restrictToBasePath);
var sqliteSqlRun = new SqliteSqlRun();
var todoContainerCreate = new TodoGroupCreate(todoDatabase);
var todoContainerRead = new TodoGroupRead(todoDatabase);
var todoContainerList = new TodoGroupList(todoDatabase);
var todoCreate = new TodoCreate(todoDatabase);
var todoRead = new TodoRead(todoDatabase);
var todoUpdate = new TodoUpdate(todoDatabase);
var askQuestionTool = new AskQuestion(consoleCommunication);

var tools = new Tool[]
{
    shellTool.Tool,
    fileReadTool.Tool,
    fileWriteTool.Tool,
    fileListTool.Tool,
    sqliteFileRun.Tool,
    sqliteSqlRun.Tool,
    todoContainerCreate.Tool,
    todoContainerRead.Tool,
    todoContainerList.Tool,
    todoCreate.Tool,
    todoRead.Tool,
    todoUpdate.Tool,
    askQuestionTool.Tool,
};

string GetMessagesFile(string id)
{
    return $"messages-{id}.json";
}

LlmAgentApi LoadAgent(string id, string apiEndpoint, string apiKey, string model, bool loadMessages = false, string? systemPrompt = null)
{
    var messagesFile = GetMessagesFile(id);

    List<JObject>? messages = null;
    if (loadMessages)
    {
        if (File.Exists(messagesFile))
        {
            messages = JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(messagesFile));
        }
    }

    if (messages == null)
    {
        if (systemPrompt == null)
        {
            systemPrompt = LlmAgents.Prompts.DefaultSystemPrompt;
        }

        messages =
        [
            JObject.FromObject(new { role = "system", content = systemPrompt })
        ];
    }

    return new LlmAgentApi(LoggerFactory, id, apiEndpoint, apiKey, model, messages, tools);
}

JObject CreateMessage(string role, string content)
{
    return JObject.FromObject(new { role, content });
}

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    if (e.SpecialKey == ConsoleSpecialKey.ControlC)
    {
        e.Cancel = true;
        cancellationTokenSource.Cancel();
    }
};

var agent = LoadAgent("agent1", apiEndpoint, apiKey, model, persistent);

void RunTool()
{
    for (int i = 0; i < tools.Length; i++)
    {
        Console.WriteLine($"{i + 1}) {tools[i].Name}");
    }

    Console.Write("Tool choice> ");
    var toolInput = Console.ReadLine();
    if (!string.IsNullOrEmpty(toolInput))
    {
        var toolChoice = int.Parse(toolInput) - 1;

        Console.WriteLine(tools[toolChoice].Schema);
        Console.WriteLine();

        Console.Write("Tool parameters (JSON)> ");
        var toolParametersInput = Console.ReadLine();
        if (!string.IsNullOrEmpty(toolParametersInput))
        {

            var toolParameters = JObject.Parse(toolParametersInput);
            var toolOutput = tools[toolChoice].Function(toolParameters);
            Console.WriteLine(toolOutput);
        }
    }
}

void ChatMode()
{
    var line = string.Empty;
    do
    {
        Console.Write("> ");
        line = Console.ReadLine();
        if (string.IsNullOrEmpty(line))
        {
            break;
        }

        var response = agent.GenerateCompletion(line);
        if (string.IsNullOrEmpty(response))
        {
            continue;
        }

        Console.WriteLine(response);

        if (persistent)
        {
            File.WriteAllText(GetMessagesFile(agent.Id), JsonConvert.SerializeObject(agent.Messages));
        }
    }
    while (!string.IsNullOrEmpty(line));
}

void RunConversation()
{
    Console.Write("Turns> ");
    var turnsInput = Console.ReadLine();
    if (string.IsNullOrEmpty(turnsInput))
    {
        return;
    }

    int turns = int.Parse(turnsInput);

    var systemPromptCommon = $"{Prompts.DefaultSystemPrompt}";
    var systemPrompt1 = $"{systemPromptCommon}";
    var systemPrompt2 = $"You are expected to guide the user.";
    var initialMessage = "Read 'PLAN.md' and start implementing the plan.";

    var messages1 = new List<JObject>()
        {
            CreateMessage("system", systemPrompt1),
        };

    var messages2 = new List<JObject>()
        {
            CreateMessage("system", systemPrompt2)
        };

    var agent1 = new LlmAgentApi(LoggerFactory, "Agent1", apiEndpoint, apiKey, model, messages1, tools);
    var agent2 = new LlmAgentApi(LoggerFactory, "Agent2", apiEndpoint, apiKey, model, messages2, tools);

    messages1.Add(CreateMessage("user", initialMessage));
    messages2.Add(CreateMessage("assistant", initialMessage));

    Console.WriteLine($"{agent2.Id}> {initialMessage}");
    Console.WriteLine($"====================================");

    var agent1Response = string.Empty;
    var agent2Response = string.Empty;
    for (int i = 0; i < turns; i++)
    {
        if (i > 0)
        {
            messages1.Add(CreateMessage("user", agent2Response));
        }

        agent1Response = agent1.GenerateCompletion(messages1);
        Console.WriteLine($"{agent1.Id}> {agent1Response}");
        Console.WriteLine($"====================================");
        Console.ReadLine();

        messages2.Add(CreateMessage("user", agent1Response));
        agent2Response = agent2.GenerateCompletion(messages2);
        Console.WriteLine($"{agent2.Id}> {agent2Response}");
        Console.WriteLine($"====================================");
        Console.ReadLine();
    }
}

void MeasureContext()
{
    var total = 0;
    foreach (var message in agent.Messages)
    {
        total += message.ToString().Length;
    }

    Console.WriteLine($"Context size: {total}");
    Console.WriteLine($"Message count: {agent.Messages.Count}");
}

void ClearContext()
{
    agent.Messages.Clear();
}

void PrintContext()
{
    foreach (var message in agent.Messages)
    {
        Console.WriteLine(message);
    }
}

void PruneContext()
{
    Console.Write("Number of messages to prune> ");
    var pruneResponse = Console.ReadLine();
    if (string.IsNullOrEmpty(pruneResponse))
    {
        return;
    }

    var pruneCount = int.Parse(pruneResponse);
    agent.Messages.RemoveRange(0, pruneCount);

    while (agent.Messages.Count > 0 && !string.Equals(agent.Messages[0].Value<string>("role"), "user"))
    {
        agent.Messages.RemoveAt(0);
    }
}

var optionRunTool = "Run tool";
var optionChatMode = "Chat mode";
var optionMeasureContext = "Measure context";
var optionClearContext = "Clear context";
var optionPrintContext = "Print context";
var optionPruneContext = "Prune context";
var optionRunConversation = "Run conversation";
var optionExit = "Exit";

var options = new string[]
{
    optionRunTool,
    optionChatMode,
    optionPrintContext,
    optionMeasureContext,
    optionClearContext,
    optionPruneContext,
    optionRunConversation,
};

var optionsMap = new Dictionary<string, Action>()
{
    { optionRunTool, RunTool },
    { optionRunConversation, RunConversation },
    { optionChatMode, ChatMode },
    { optionPrintContext, PrintContext },
    { optionMeasureContext, MeasureContext },
    { optionClearContext, ClearContext },
    { optionPruneContext, PruneContext },
};

while (true)
{
    if (cancellationTokenSource.IsCancellationRequested)
    {
        cancellationTokenSource = new CancellationTokenSource();
    }

    for (int i = 0; i < options.Length; i++)
    {
        Console.WriteLine($"{i + 1}) {options[i]}");
    }

    Console.WriteLine($"0) {optionExit}");

    Console.Write("Choice> ");
    var input = Console.ReadLine();
    if (string.IsNullOrEmpty(input))
    {
        return;
    }

    Console.WriteLine();

    if (string.Equals(input, "0"))
    {
        break;
    }

    var choice = int.Parse(input) - 1;
    optionsMap[options[choice]]();
}

public partial class Program
{
    public static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddConsole();
    });

    public static readonly ILogger Log = LoggerFactory.CreateLogger(nameof(Program));
}
