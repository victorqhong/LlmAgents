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

string GetMessagesFile(string id)
{
    return $"messages-{id}.json";
}

LlmAgentApi CreateAgent(string id, string apiEndpoint, string apiKey, string model, bool loadMessages = false)
{
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
    agent.AddTool(shellTool.Tool);
    agent.AddTool(fileReadTool.Tool);
    agent.AddTool(fileWriteTool.Tool);
    agent.AddTool(sqliteSqlRun.Tool);
    agent.AddTool(sqliteFileRun.Tool);
    agent.AddTool(todoContainerCreate.Tool);
    agent.AddTool(todoCreate.Tool);
    agent.AddTool(todoRead.Tool);
    agent.AddTool(todoContainerList.Tool);

    return agent;
}

var agent1 = CreateAgent("agent1", apiEndpoint, apiKey, model);

var line = string.Empty;
do
{
    line = Console.ReadLine();
    if (string.IsNullOrEmpty(line))
    {
        break;
    }

    var response = agent1.GenerateCompletion(line);

    Console.WriteLine(response);
}
while (!string.IsNullOrEmpty(line));

File.WriteAllText(GetMessagesFile(agent1.Id), JsonConvert.SerializeObject(agent1.Messages));

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

