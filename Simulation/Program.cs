using System;
using Simulation;

var apiEndpoint = "";
var apiKey = "";

var systemPrompt = "You are a Software Engineer with over 10 years of professional experience. You are proficient at programming and communication.";

LlmAgentApi agent1 = new LlmAgentApi(apiEndpoint, apiKey, "gpt-4o", systemPrompt);
var response = agent1.GenerateCompletion("Run a shell command to get the current user on a linux machine");

Console.WriteLine(response);
Console.ReadLine();

