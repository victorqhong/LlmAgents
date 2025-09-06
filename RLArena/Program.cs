using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.Content;
using Microsoft.Extensions.Logging;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var consoleCommunication = new ConsoleCommunication();
var apiEndpoint = "http://llamacpp.home.local:8080/v1/chat/completions";
var apiKey = "sk-none";
var apiModel = "llamacpp";
var contextSize = 8196;
var maxCompletionTokens = 8196;
var agentId = "gpt-4.1";
var workingDirectory = Environment.CurrentDirectory;
var storageDirectory = Environment.CurrentDirectory; 
var toolsFilePath = "tools.json";

var agent = await LlmAgent.CreateAgent(loggerFactory, consoleCommunication, apiEndpoint, apiKey, apiModel, contextSize, maxCompletionTokens, agentId, workingDirectory, storageDirectory, toolsFilePath: toolsFilePath);
var llmApi = agent.llmApi;

var content = new MessageContentText() { Text = "What tools are available?" };
var message = LlmApiOpenAi.GetMessage([content]);
var completion = await llmApi.GenerateCompletion([message]); 
Console.WriteLine(completion);

var states = Enum.GetNames<States>();
var actions = Enum.GetNames<Actions>();

double[,] qTable = new double[states.Length, actions.Length];
double alpha = 0.1; // Learning rate
double gamma = 0.9; // Discount factor
double epsilon = 0.1; // Exploration rate
Random rand = new();

var episodes = 10;
for (int i = 0; i < episodes; i++)
{
  var state = States.BuildFailTestsFailFeatureIncomplete;
  while (state != States.Done)
  {
    var action = (rand.NextDouble() < epsilon) ? Enum.Parse<Actions>(actions[rand.Next(0, actions.Length)]) : BestAction(state);
    var newState = NextState(state, action);
    var reward = CalculateReward(newState);
    qTable[(int)state, (int)action] += alpha * (reward + gamma * MaxQ(newState) - qTable[(int)state, (int)action]);
    state = newState;
  }
}

double MaxQ(States state)
{
  var maxQ = qTable[(int)state, 0];
  for (int i = 1; i < actions.Length; i++)
  {
    maxQ = Math.Max(maxQ, qTable[(int)state, i]);
  }

  return maxQ;
}

int ArgMaxQ(States state)
{
  var maxIndex = 0;
  var maxQ = qTable[(int)state, maxIndex];
  for (int i = 1; i < actions.Length; i++)
  {
    if (qTable[(int)state, i] > maxQ)
    {
      maxQ = qTable[(int)state, i];
      maxIndex = i;
    }
  }

  return maxIndex;
}

Actions BestAction(States state)
{
  return Enum.Parse<Actions>(actions[ArgMaxQ(state)]);
}

States NextState(States currentState, Actions action)
{
  var buildFail = BuildFail();
  var testsFail = TestsFail();
  var featureIncomplete = FeatureIncomplete();

  if (buildFail && testsFail && featureIncomplete)
  {
    return States.BuildFailTestsFailFeatureIncomplete;
  }
  else if (!buildFail && testsFail && featureIncomplete)
  {
    return States.BuildPassTestsFailFeatureIncomplete;
  }
  else if (!buildFail && !testsFail && featureIncomplete)
  {
    return States.BuildPassTestsPassFeatureIncomplete;
  }
  else if (!buildFail && !testsFail && !featureIncomplete)
  {
    return States.Done;
  }

  throw new Exception();
}

double CalculateReward(States state)
{
    return state switch
    {
        States.BuildFailTestsFailFeatureIncomplete => -1,
        States.BuildPassTestsFailFeatureIncomplete => -0.5,
        States.BuildPassTestsPassFeatureIncomplete => -0.1,
        States.Done => 1,
        _ => 0,
    };
}

bool BuildFail()
{
  return RunCommand("dotnet", "build", workingDirectory);
}

bool TestsFail()
{
  return RunCommand("dotnet", "test", workingDirectory);
}

bool FeatureIncomplete()
{
  return true;
}

bool RunCommand(string fileName, string arguments, string? workingDirectory = null)
{
  if (string.IsNullOrEmpty(workingDirectory))
  {
    workingDirectory = Environment.CurrentDirectory;
  }

  try
  {
    var process = new System.Diagnostics.Process();
    process.StartInfo.FileName = fileName;
    process.StartInfo.Arguments = arguments;
    process.StartInfo.WorkingDirectory = workingDirectory;
    process.Start();
    process.WaitForExit();

    return process.ExitCode == 0;
  }
  catch
  {
    return false;
  }
}

enum States
{
  BuildFailTestsFailFeatureIncomplete = 0,
  BuildPassTestsFailFeatureIncomplete = 1,
  BuildPassTestsPassFeatureIncomplete = 2,
  Done = 3
}

enum Actions
{
  ReadFile = 0,
  WriteFile = 1,
  ShellCommand = 2,
}
