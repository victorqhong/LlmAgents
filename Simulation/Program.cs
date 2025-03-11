using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Simulation;
using Simulation.Todo;
using Simulation.Tools;

var apiEndpoint = "";
var apiKey = "";
var model = "gpt-4o";

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
    Console.WriteLine("apiEndpoint or apiKey is null or empty.");
    return;
}

var defaultSystemPrompt = "You are a Software Engineer with over 10 years of professional experience. You are proficient at programming and communication. You are expected to implement code changes. If something needs clarification, how to proceed, or given a choice, use the 'question_ask' tool.";
var toolsPrompt = "Summarize the tools available and their parameters";
var questionairePrompt = "Write a questionaire to gather requirements for a new software project minimum viable product. Save the file to MVP.md";
var planPrompt = "Read the file 'MVP.md' and generate an implementation plan, and save the file to PLAN.md";
var todoPrompt = "Read the file 'PLAN.md' and create todos in appropriate groups. Each phase should have one or more todos.";

var todoDatabase = new TodoDatabase("todo.db");
var basePath = Environment.CurrentDirectory;
var restrictToBasePath = true;

var shellTool = new Shell(workingDirectory: basePath);
var fileReadTool = new FileRead(basePath, restrictToBasePath);
var fileWriteTool = new FileWrite(basePath, restrictToBasePath);
var fileListTool = new FileList(basePath, restrictToBasePath);
var sqliteFileRun = new SqliteFileRun();
var sqliteSqlRun = new SqliteSqlRun();
var todoContainerCreate = new TodoGroupCreate(todoDatabase);
var todoContainerRead = new TodoGroupRead(todoDatabase);
var todoContainerList = new TodoGroupList(todoDatabase);
var todoCreate = new TodoCreate(todoDatabase);
var todoRead = new TodoRead(todoDatabase);
var todoUpdate = new TodoUpdate(todoDatabase);
var askQuestionTool = new AskQuestion();

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
            systemPrompt = defaultSystemPrompt;
        }

        messages =
        [
            JObject.FromObject(new { role = "system", content = systemPrompt })
        ];
    }

    return new LlmAgentApi(id, apiEndpoint, apiKey, model, messages, tools);
}

JObject CreateMessage(string role, string content)
{
    return JObject.FromObject(new { role, content });
}

var agent = LoadAgent("agent1", apiEndpoint, apiKey, model, true);
while (true)
{
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
        optionRunConversation,
        optionChatMode,
        optionPrintContext,
        optionMeasureContext,
        optionClearContext,
        optionPruneContext,
    };

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
    var option = options[choice];
    if (string.Equals(option, optionRunTool))
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
    else if (string.Equals(option, optionChatMode))
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

            Console.WriteLine(response);

            File.WriteAllText(GetMessagesFile(agent.Id), JsonConvert.SerializeObject(agent.Messages));
        }
        while (!string.IsNullOrEmpty(line));
    }
    else if (string.Equals(option, optionMeasureContext))
    {
        var total = 0;
        foreach (var message in agent.Messages)
        {
            total += message.ToString().Length;
        }

        Console.WriteLine($"Context size: {total}");
        Console.WriteLine($"Message count: {agent.Messages.Count}");
    }
    else if (string.Equals(option, optionClearContext))
    {
        agent.Messages.Clear();
    }
    else if (string.Equals(option, optionPrintContext))
    {
        foreach (var message in agent.Messages)
        {
            Console.WriteLine(message);
        }
    }
    else if (string.Equals(option, optionPruneContext))
    {
        Console.Write("Number of messages to prune> ");
        var pruneResponse = Console.ReadLine();
        if (string.IsNullOrEmpty(pruneResponse))
        {
            continue;
        }

        var pruneCount = int.Parse(pruneResponse);
        agent.Messages.RemoveRange(0, pruneCount);

        while (agent.Messages.Count > 0 && !string.Equals(agent.Messages[0].Value<string>("role"), "user"))
        {
            agent.Messages.RemoveAt(0);
        }
    }
    else if (string.Equals(option, optionRunConversation))
    {
        Console.Write("Turns> ");
        var turnsInput = Console.ReadLine();
        if (string.IsNullOrEmpty(turnsInput))
        {
            continue;
        }

        int turns = int.Parse(turnsInput);

        var systemPromptCommon = $"{defaultSystemPrompt}";
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

        var agent1 = new LlmAgentApi("Agent1", apiEndpoint, apiKey, model, messages1, tools);
        var agent2 = new LlmAgentApi("Agent2", apiEndpoint, apiKey, model, messages2, tools);

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
}

public partial class Program
{
    public static readonly ILoggerFactory loggerFactory;

    static Program()
    {
        loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddConsole());
    }
}

