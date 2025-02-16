using System;
using Simulation;

var apiEndpoint = "";
var apiKey = "";
var model = "gpt-4o";

var credentialsFile = System.Environment.GetEnvironmentVariable("LLM_CREDENTIALS_FILE");
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

var systemPrompt = "You are a Software Engineer with over 10 years of professional experience. You are proficient at programming and communication.";

LlmAgentApi agent1 = new LlmAgentApi(apiEndpoint, apiKey, "gpt-4o", systemPrompt);
var response = agent1.GenerateCompletion("Run a shell command to get the current user on a linux machine");

Console.WriteLine(response);
Console.ReadLine();

