using System;
using System.Runtime.InteropServices;
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

var shellTool = new Shell();
var fileReadTool = new FileRead();
var fileWriteTool = new FileWrite();
var sqliteFileRun = new SqliteFileRun();
var sqliteSqlRun = new SqliteSqlRun();
var todoContainerCreate = new TodoContainerCreate(todoDatabase);
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
var response = agent1.GenerateCompletion("Summarize the tools you can call and their parameters.");

Console.WriteLine(response);
Console.ReadLine();

