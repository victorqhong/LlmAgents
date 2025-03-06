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

var systemPrompt = "You are a Software Engineer with over 10 years of professional experience. You are proficient at programming and communication.";
var toolsPrompt = "Summarize the tools available and their parameters";
var questionairePrompt = "Write a questionaire to gather requirements for a new software project minimum viable product. Save the file to MVP.md";
var planPrompt = "Read the file 'MVP.md' and generate an implementation plan, and save the file to PLAN.md";
var todoPrompt = "Read the file 'PLAN.md' and create todos in appropriate groups. Each phase should have one or more todos.";

var todoDatabase = new TodoDatabase("todo.db");
var basePath = Environment.CurrentDirectory;

var shellTool = new Shell();
var fileReadTool = new FileRead(basePath);
var fileWriteTool = new FileWrite(basePath);
var sqliteFileRun = new SqliteFileRun();
var sqliteSqlRun = new SqliteSqlRun();
var todoContainerCreate = new TodoGroupCreate(todoDatabase);
var todoContainerRead = new TodoGroupRead(todoDatabase);
var todoContainerList = new TodoGroupList(todoDatabase);
var todoCreate = new TodoCreate(todoDatabase);
var todoRead = new TodoRead(todoDatabase);
var todoUpdate = new TodoUpdate(todoDatabase);

var tools = new Tool[]
{
    shellTool.Tool,
    fileReadTool.Tool,
    fileWriteTool.Tool,
    sqliteFileRun.Tool,
    sqliteSqlRun.Tool,
    todoContainerCreate.Tool,
    todoContainerRead.Tool,
    todoContainerList.Tool,
    todoCreate.Tool,
    todoRead.Tool,
    todoUpdate.Tool
};

string GetMessagesFile(string id)
{
    return $"messages-{id}.json";
}

LlmAgentApi CreateAgent(string id, string apiEndpoint, string apiKey, string model, bool loadMessages = false)
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
        messages =
        [
            JObject.FromObject(new { role = "system", content = systemPrompt })
        ];
    }

    var agent = new LlmAgentApi(id, apiEndpoint, apiKey, model, messages);
    agent.AddTool(tools);

    return agent;
}

var agent1 = CreateAgent("agent1", apiEndpoint, apiKey, model, true);

var exit = false;
while (!exit)
{
    var optionRunTool = "Run tool";
    var optionChatMode = "Chat mode";
    var optionMeasureContext = "Measure context";
    var optionClearContext = "Clear context";
    var optionPrintContext = "Print context";
    var optionExit = "Exit";

    var options = new string[]
    {
        optionRunTool,
        optionChatMode,
        optionPrintContext,
        optionMeasureContext,
        optionClearContext,
        optionExit
    };

    for (int i = 0; i < options.Length; i++)
    {
        Console.WriteLine($"{i + 1}) {options[i]}");
    }

    Console.Write("Choice> ");
    var input = Console.ReadLine();
    if (string.IsNullOrEmpty(input))
    {
        return;
    }

    Console.WriteLine();

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

            var response = agent1.GenerateCompletion(line);

            Console.WriteLine(response);

            File.WriteAllText(GetMessagesFile(agent1.Id), JsonConvert.SerializeObject(agent1.Messages));
        }
        while (!string.IsNullOrEmpty(line));
    }
    else if (string.Equals(option, optionMeasureContext))
    {
        var total = 0;
        foreach (var message in agent1.Messages)
        {
            total += message.ToString().Length;
        }

        Console.WriteLine($"Context size: {total}");
    }
    else if (string.Equals(option, optionClearContext))
    {
        agent1.Messages.Clear();
    }
    else if (string.Equals(option, optionPrintContext))
    {
        foreach (var message in agent1.Messages)
        {
            Console.WriteLine(message);
        }
    }
    else if (string.Equals(option, optionExit))
    {
        exit = true;
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

