using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Simulation;
using Simulation.Todo;
using Simulation.Tools;

var apiEndpoint = "";
var apiKey = "";
var model = "gpt-4o";

var environmentVariableTarget = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? EnvironmentVariableTarget.User : EnvironmentVariableTarget.Process;
var credentialsFile = Environment.GetEnvironmentVariable("LLM_CREDENTIALS_FILE", environmentVariableTarget);
if (System.IO.File.Exists(credentialsFile))
{
    var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(credentialsFile));
    apiEndpoint = $"{json.Value<string>("AZURE_OPENAI_ENDPOINT")}/openai/deployments/{model}/chat/completions?api-version=2024-08-01-preview";
    apiKey = json.Value<string>("AZURE_OPENAI_API_KEY");
}

if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("apiEndpoint or apiKey is null or empty.");
    return;
}

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

var systemPrompt = "You are a Software Engineer with over 10 years of professional experience. You are proficient at programming and communication.";

LlmAgentApi agent1 = new LlmAgentApi(apiEndpoint, apiKey, "gpt-4o", systemPrompt);
agent1.AddTool(shellTool.Tool);
agent1.AddTool(fileReadTool.Tool);
agent1.AddTool(fileWriteTool.Tool);
agent1.AddTool(sqliteSqlRun.Tool);
agent1.AddTool(sqliteFileRun.Tool);
agent1.AddTool(todoContainerCreate.Tool);
agent1.AddTool(todoCreate.Tool);
agent1.AddTool(todoRead.Tool);
agent1.AddTool(todoContainerList.Tool);

var toolsPrompt = "Summarize the tools available and their parameters";
var questionairePrompt = "Write a questionaire to gather requirements for a new software project minimum viable product. Save the file to MVP.md";
var planPrompt = "Read the file 'MVP.md' and generate an implementation plan, and save the file to PLAN.md";
var todoPrompt = "Read the file 'PLAN.md' and create todos in appropriate groups. Each phase should have one or more todos.";

var response = agent1.GenerateCompletion(todoPrompt);

Console.WriteLine(response);
Console.ReadLine();

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

