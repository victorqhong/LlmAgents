using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Parquet;
using RLArena;
using System;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

var consoleCommunication = new ConsoleCommunication()
{
    NullOutput = false
};
var apiEndpoint = "http://llamacpp.home.local:8080/v1/chat/completions";
var apiKey = "sk-none";
var apiModel = "llamacpp";
var contextSize = 32768;
var maxCompletionTokens = 8196;
var agentId = "gpt-4.1";
var workingDirectory = @"C:\Users\victo\Code\agents\agents\rlarena";
var storageDirectory = Environment.CurrentDirectory; 
var toolsFilePath = "tools.json";
States state = States.BuildFail;

//using var fs = System.IO.File.OpenRead(@"C:\Users\victo\Code\LeetCodeDataset\default\train\0000.parquet");
//using var reader = await ParquetReader.CreateAsync(fs);

//var problemDescriptionDf = reader.Schema.FindDataField("problem_description");
//var starterCodeDf = reader.Schema.FindDataField("starter_code");

//for (int i = 0; i < reader.RowGroupCount; i++)
//{
//    using var rowGroupReader = reader.OpenRowGroupReader(i);
//    var problemDescriptionData = await rowGroupReader.ReadColumnAsync(problemDescriptionDf);
//    var starterCodeData = await rowGroupReader.ReadColumnAsync(starterCodeDf);
//}

var goal = "Create a dotnet console application in C# that writes 'hello world'.";
//var systemPrompt = $"You are running in an environment where the user will not respond to any of your messages. Pause after each message for input from the user. Use a single tool at a time and pause after each tool used.\n\nThe following commands will be run in the {workingDirectory} directory to build, test, and run the project.\nBuild: dotnet build\nTest: dotnet test\nRun: dotnet run\nThese exact commands will be used to evaluate whether the project is completed.";
var systemPrompt = $"Goal: {goal}";

var agent = await LlmAgent.CreateAgent(loggerFactory, consoleCommunication, apiEndpoint, apiKey, apiModel, contextSize, maxCompletionTokens, agentId, workingDirectory, storageDirectory, toolsFilePath: toolsFilePath, systemPrompt: systemPrompt);
agent.StreamOutput = true;

var states = Enum.GetNames<States>();
var actions = Enum.GetNames<Actions>();

var actionBuffer = new CircularBuffer<Actions>(5);

double[,] qTable = new double[states.Length, actions.Length];
double alpha = 0.1; // Learning rate
double gamma = 0.9; // Discount factor
double epsilon = 0.3; // Exploration rate
Random rand = new();

agent.ToolEventBus?.SubscribeToolEvent<Tool>(async e =>
{
    var toolName = e.Sender.Name;
    var action = ToolNameToAction(toolName);

    var newState = EvaluateState();
    var reward = CalculateReward(newState, state, action);
    qTable[(int)state, (int)action] += alpha * (reward + gamma * MaxQ(newState) - qTable[(int)state, (int)action]);
    state = newState;
});

bool loadResults = false;
bool saveResults = false;

if (loadResults && File.Exists("qtable.bin"))
{
    var br = new BinaryReader(File.OpenRead("qtable.bin"));
    for (int i = 0; i < qTable.GetLength(0); i++)
    {
        for (int j = 0; j < qTable.GetLength(1); j++)
        {
            qTable[i, j] = br.ReadDouble();
        }
    }
}

var agentMode = false;

for (int i = 0; i < 10; i++)
{
    ResetEnvironment();

    epsilon = (1.0 - i / 9.0) - (1.0 - i / 9.0) * 0.5 + 0.1;

    state = States.BuildFail;
    while (FeatureIncomplete())
    {
        if (agentMode)
        {
            await RunAgentAction(state);
        }
        else
        {
            Actions action;
            if (rand.NextDouble() < epsilon)
            {
                action = Enum.Parse<Actions>(actions[rand.Next(0, actions.Length)]);
            }
            else
            {
                action = BestAction(state);
            }

            actionBuffer.Add(action);

            await RunAction(state, action);

            var newState = EvaluateState();
            var reward = CalculateReward(newState, state, action);
            qTable[(int)state, (int)action] += alpha * (reward + gamma * MaxQ(newState) - qTable[(int)state, (int)action]);
            state = newState;
        }
    }

    agent.Messages.Clear();
    agent.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));
}

if (saveResults)
{
    var bw = new BinaryWriter(File.OpenWrite("qtable.bin"));
    for (int i = 0; i < qTable.GetLength(0); i++)
    {
        for (int j = 0; j < qTable.GetLength(1); j++)
        {
            bw.Write(qTable[i, j]);
        }
    }

    bw.Close();
}

void ResetEnvironment()
{
    foreach (var directory in Directory.EnumerateDirectories(workingDirectory, "*.*", SearchOption.AllDirectories))
    {
        Directory.Delete(directory, true);
    }

    foreach (var file in Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories))
    {
        File.Delete(file);
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
        var q = qTable[(int)state, i];
        if (q > maxQ)
        {
            maxQ = q;
            maxIndex = i;
        }
        else if (Math.Abs(q - maxQ) < 0.001 && rand.NextDouble() < epsilon)
        {
            maxQ = q;
            maxIndex = i;
        }
    }

    return maxIndex;
}

Actions BestAction(States state)
{
  return Enum.Parse<Actions>(actions[ArgMaxQ(state)]);
}

async Task RunAgentAction(States state)
{
    string message = string.Empty;
    if (state == States.BuildFail)
    {
        message = "Current state: running 'dotnet_build' failed.";
    }
    else
    {
        message = "Current state: running 'dotnet_run' did not output 'hello world'";
    }

    var content = new MessageContentText() { Text = $"{message}\nUse the most appropriate tool." };
    await agent.SendMessage([content], agent.Tools, "required");
}

async Task RunAction(States state, Actions action)
{
    string message = string.Empty;
    if (state == States.BuildFail)
    {
        message = "Current state: running 'dotnet_build' failed.";
    }
    else
    {
        message = "Current state: running 'dotnet_run' did not output 'hello world'";
    }

    var toolName = ActionToToolName(action);

    var t = agent.Tools.Where(tool => string.Equals(toolName, tool.Name)).ToList();
    var content = new MessageContentText() { Text = $"{message}\nUse the {toolName} tool." };
    //var content = new MessageContentText() { Text = JObject.FromObject(new { currentState = state.ToString(), useTool = toolName }).ToString(Formatting.None) };
    await agent.SendMessage([content], t, "required");
}

string ActionToToolName(Actions action)
{
    return Enum.GetName<Actions>(action);
}

Actions ToolNameToAction(string toolName)
{
    return Enum.Parse<Actions>(toolName);
}

    double CalculateReward(States newState, States oldState, Actions action)
{
    if (newState == States.BuildFail && oldState == States.GoalIncomplete)
    {
        if (action == Actions.file_delete)
        {
            return -0.5;
        }

        return -0.3;
    }

    if (newState == States.BuildFail)
    {
        return 0.1;
    }
    else if (newState == States.GoalIncomplete)
    {
        return 0.1;
    }
    else if (newState == States.GoalComplete)
    {
        return 1;
    }
    else
    {
        throw new Exception();
    }
}

States EvaluateState()
{
    var buildFail = BuildFail();
    var featureIncomplete = FeatureIncomplete();

    if (buildFail)
    {
        return States.BuildFail;
    }
    else if (!buildFail && featureIncomplete)
    {
        return States.GoalIncomplete;
    }
    else
    {
        return States.GoalComplete;
    }

    throw new Exception();
}

bool BuildFail()
{
    return RunCommand("dotnet", "build", workingDirectory, out _);
}

bool TestsFail()
{
    return RunCommand("dotnet", "test", workingDirectory, out _);
}

bool FeatureIncomplete()
{
    RunCommand("dotnet", "run", workingDirectory, out var stdout);
    return !string.Equals(stdout.Trim(), "hello world");
}

bool RunCommand(string fileName, string arguments, string? workingDirectory, out string? stdout)
{
    if (string.IsNullOrEmpty(workingDirectory))
    {
        workingDirectory = Environment.CurrentDirectory;
    }

    var process = new System.Diagnostics.Process();
    try
    {
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        process.WaitForExit(30_000);
        if (!process.HasExited)
        {
            process.Kill(true);
        }

        stdout = process.StandardOutput.ReadToEnd();

        return process.ExitCode != 0;
    }
    catch
    {
        stdout = null;
        return false;
    }
}

enum States
{
  BuildFail = 0,
  GoalIncomplete = 1,
  GoalComplete = 2,
}

enum Actions
{
    file_read = 0,
    file_write = 1,
    file_list = 2,
    file_delete = 3,
    dotnet_build = 4,
    dotnet_run = 5,
}
