
using RLArena;

var agentParameters = new AgentParameters
{
    ApiEndpoint = "http://llamacpp.home.local:8080/v1/chat/completions",
    ApiKey = "sk-none",
    ApiModel = "llamacpp",
    ContextSize = 131072,
    MaxCompletionTokens = 8196,
    AgentId = "gpt-4.1",
    WorkingDirectory = @"C:\Users\victo\Code\agents\agents\rlarena",
    StorageDirectory = System.Environment.CurrentDirectory,
    ToolsFilePath = "tools.json",
};

var qTableFile = "qtable.bin";
var applicationInsightsConnectionString = System.Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
var runId = DateTime.Now.ToUniversalTime().ToString("u");

var train = true;
var evaluate = false;

if (train)
{
    await new Train(agentParameters, qTableFile, applicationInsightsConnectionString, runId).TrainAgent();
}

if (evaluate)
{
    await new Evaluate(agentParameters, qTableFile, applicationInsightsConnectionString, runId).EvaluateRLAgent();
}