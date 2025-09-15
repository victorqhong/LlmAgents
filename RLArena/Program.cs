using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RLArena;
using System.Diagnostics;

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
bool resettingEnvironment = false;

var goal = "Create a dotnet console application in C# that writes 'hello world'.";
var systemPrompt = $"Goal: {goal}";

var agent = await LlmAgent.CreateAgent(loggerFactory, consoleCommunication, apiEndpoint, apiKey, apiModel, contextSize, maxCompletionTokens, agentId, workingDirectory, storageDirectory, toolsFilePath: toolsFilePath, systemPrompt: systemPrompt);
agent.StreamOutput = true;

var states = Enum.GetNames<States>();
var actions = Enum.GetNames<Actions>();

var actionBuffer = new CircularBuffer<Actions>(5);

var qTable = new Dictionary<string, double[]>();
double alpha = 0.1; // Learning rate
double gamma = 0.9; // Discount factor
double epsilon = 0.3; // Exploration rate
Random rand = new();

agent.ToolEventBus?.SubscribeToolEvent<Tool>(async e =>
{
    if (e.Sender.Name.Equals("directory_change"))
    {
        var tce = e as ToolCallEvent;
        workingDirectory = tce.Result.Value<string>("currentDirectory");
    }

    if (resettingEnvironment)
    {
        return;
    }

    if (state == States.GoalComplete)
    {
        return;
    }

    var toolName = e.Sender.Name;
    var action = ToolNameToAction(toolName);

    var newState = EvaluateState();
    var reward = CalculateReward(newState, state, action, actionBuffer);
    var (qVals, key) = GetQTableEntry(state, actionBuffer);
    actionBuffer.Add(action);
    qVals[(int)action] += alpha * (reward + gamma * MaxQ(newState, actionBuffer) - qVals[(int)action]);
    state = newState;
});

bool loadResults = true;
bool saveResults = false;
bool agentMode = false;

if (loadResults)
{
    LoadQTable("qtable.bin", out qTable);
}

int episodes = 100;
int step = 0;

if (agentMode)
{
    for (int i = 0; i < episodes; i++)
    {
        Console.WriteLine($"======================== Episode {i + 1} ========================");
        ResetEnvironment();

        state = States.BuildFail;
        actionBuffer = new CircularBuffer<Actions>(5); // Reset buffer each episode
        while (FeatureIncomplete())
        {
            if (step >= 60)
            {
                break;
            }

            Console.WriteLine($"======================== Episode {i + 1} Step {step + 1} ========================");
            await RunAgentAction(state);
            step++;
        }

        step = 0;

        agent.Messages.Clear();
        agent.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));

        if (saveResults)
        {
            SaveQTable("qtable.bin", qTable);
        }
    }
}

for (int i = 0; i < episodes; i++)
{
    state = States.BuildFail;

    Console.WriteLine($"======================== Episode {i + 1} ========================");
    ResetEnvironment();

    //epsilon = (1.0 - i / (float)(episodes - 1)) * 0.4 + 0.05;
    epsilon = 0;

    actionBuffer = new CircularBuffer<Actions>(5); // Reset buffer each episode
    while (state != States.GoalComplete)
    {
        if (step >= 60)
        {
            break;
        }

        Actions action;
        if (rand.NextDouble() < epsilon)
        {
            action = Enum.Parse<Actions>(actions[rand.Next(0, actions.Length)]);
        }
        else
        {
            action = BestAction(state, actionBuffer);
        }

        Console.WriteLine($"======================== Episode {i + 1} Step {step + 1} Action {action} ========================");
        await RunAction(state, action);

        if (action == Actions.think)
        {
            var newState = EvaluateState();
            var reward = CalculateReward(newState, state, action, actionBuffer);
            var (qVals, key) = GetQTableEntry(state, actionBuffer);
            actionBuffer.Add(action);
            qVals[(int)action] += alpha * (reward + gamma * MaxQ(newState, actionBuffer) - qVals[(int)action]);
            state = newState;
        }

        step++;
    }

    agent.Messages.Clear();
    agent.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));

    if (saveResults)
    {
        SaveQTable("qtable.bin", qTable);
    }
}

SaveQTable("qtable.bin", qTable);
LoadQTable("qtable.bin", out var _);

Debugger.Launch();

void LoadQTable(string path, out Dictionary<string, double[]> qTable)
{
    qTable = [];
    if (!File.Exists(path))
    {
        return;
    }

    using var br = new BinaryReader(File.OpenRead(path));
    var count = br.ReadInt32();
    for (int i = 0; i < count; i++)
    {
        var key = br.ReadString();

        var value = new double[actions.Length];
        for (int j = 0; j < value.Length; j++)
        {
            value[j] = br.ReadDouble();
        }

        if (qTable.ContainsKey(key))
        {
            throw new Exception();
        }
        else
        {
            qTable.Add(key, value);
        }
    }
}

void SaveQTable(string path, Dictionary<string, double[]> qTable)
{
    using var bw = new BinaryWriter(File.OpenWrite(path));
    bw.Write(qTable.Count);
    foreach (var kvp in qTable)
    {
        bw.Write(kvp.Key);
        for (int j = 0; j < actions.Length; j++)
        {
            bw.Write(kvp.Value[j]);
        }
    }
}

void ResetEnvironment()
{
    resettingEnvironment = true;

    step = 0;
    workingDirectory = @"C:\Users\victo\Code\agents\agents\rlarena";
    agent.FindTool("directory_change")?.Invoke(JObject.FromObject(new { path = workingDirectory }));
    foreach (var directory in Directory.EnumerateDirectories(workingDirectory, "*.*", SearchOption.AllDirectories))
    {
        Directory.Delete(directory, true);
    }

    foreach (var file in Directory.EnumerateFiles(workingDirectory, "*.*", SearchOption.AllDirectories))
    {
        File.Delete(file);
    }

    resettingEnvironment = false;
}

string StateToKey(States state, params Actions[] actions)
{
    return $"{state}|{string.Join('|', actions)}";
}

// Helper for Q-table key
(double[], string) GetQTableEntry(States state, CircularBuffer<Actions> buffer)
{
    var items = buffer.GetItems();

    string key;
    switch (items.Count)
    {
        case 0:
            key = StateToKey(state);
            break;
        case 1:
            key = StateToKey(state, items[0]);
            break;
        case 2:
            key = StateToKey(state, items[0], items[1]);
            break;
        case 3:
            key = StateToKey(state, items[0], items[1], items[2]);
            break;
        case 4:
            key = StateToKey(state, items[0], items[1], items[2], items[3]);
            break;
        case 5:
            key = StateToKey(state, items[0], items[1], items[2], items[3], items[4]);
            break;
        default:
            throw new Exception();
    }

    if (!qTable.TryGetValue(key, out var qVals))
    {
        qVals = new double[actions.Length];
        qTable[key] = qVals;
    }

    return (qVals, key);
}

double[] GetQValues(States state, CircularBuffer<Actions> buffer)
{
    return GetQTableEntry(state, buffer).Item1;
}

double MaxQ(States state, CircularBuffer<Actions> buffer)
{
    var qVals = GetQValues(state, buffer);
    return qVals.Max();
}

int ArgMaxQ(States state, CircularBuffer<Actions> buffer)
{
    var qVals = GetQValues(state, buffer);
    double maxQ = qVals[0];
    int maxIdx = 0;
    for (int i = 1; i < qVals.Length; i++)
    {
        if (qVals[i] > maxQ)
        {
            maxQ = qVals[i];
            maxIdx = i;
        }
    }
    return maxIdx;
}

Actions BestAction(States state, CircularBuffer<Actions> buffer)
{
    return (Actions)ArgMaxQ(state, buffer);
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
        message = "Current state: running 'dotnet_run' did not output exactly 'hello world'";
    }

    var content = new MessageContentText() { Text = $"{message}\nUse the most appropriate tool." };
    await agent.SendMessage([content], agent.Tools, "auto");
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
        message = "Current state: running 'dotnet_run' did not output exactly 'hello world'";
    }

    var toolName = ActionToToolName(action);
    List<Tool>? tools = null;
    string? toolChoice = null;
    if (action == Actions.think)
    {
        tools = null;
        toolChoice = "auto";
    }
    else
    {
        var t = agent.Tools.Where(tool => string.Equals(toolName, tool.Name)).ToList();
        tools = t;
        toolChoice = "auto";
    }

    var content = new MessageContentText() { Text = $"{message}\nUse the {toolName} tool." };
    await agent.SendMessage([content], tools, toolChoice);
}

string ActionToToolName(Actions action)
{
    return Enum.GetName<Actions>(action);
}

Actions ToolNameToAction(string toolName)
{
    return Enum.Parse<Actions>(toolName);
}

double CalculateReward(States newState, States oldState, Actions action, CircularBuffer<Actions> buffer)
{
    if (newState == States.GoalComplete)
    {
        return 1;
    }

    var items = buffer.GetItems();

    if (newState == States.GoalIncomplete && oldState == States.BuildFail)
    {
        return 0.5;
    }

    if (newState == States.GoalIncomplete && action == Actions.dotnet_run)
    {
        return 0.2;
    }

    if (newState == States.BuildFail && action == Actions.file_write)
    {
        return 0.1;
    }

    if (newState == States.BuildFail && oldState == States.GoalIncomplete)
    {
        if (action == Actions.file_delete)
        {
            return -0.5;
        }
        else if (action == Actions.folder_delete)
        {
            return -0.5;
        }

        return -0.3;
    }

    if (newState == States.BuildFail)
    {
        if (action == Actions.file_delete)
        {
            return -0.1;
        }
        else if (action == Actions.folder_delete)
        {
            return -0.1;
        }
    }

    if (items.Count > 1 && action == Actions.think && items[^1] == Actions.think)
    {
        return -0.5;
    }

    if (items.Count >= 4 && action == items[^1] && items[^1] == items[^2] && items[^2] == items[^3] && items[^3] == items[^4])
    {
        return -0.5;
    }
    else if (items.Count >= 3 && action == items[^1] && items[^1] == items[^2] && items[^2] == items[^3])
    {
        return -0.3;
    }
    else if (items.Count >= 2 && action == items[^1] && items[^1] == items[^2])
    {
        return -0.2;
    }
    else if (items.Count >= 1 && action == items[^1])
    {
        return -0.1;
    }

    if (newState == States.BuildFail)
    {
        return -0.01;
    }
    else if (newState == States.GoalIncomplete)
    {
        return -0.01;
    }

    throw new Exception();
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
    return !string.Equals(stdout?.Trim(), "hello world");
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
    think = 0,
    file_read = 1,
    file_write = 2,
    file_list = 3,
    file_delete = 4,
    folder_create = 5,
    folder_delete = 6,
    dotnet_build = 7,
    dotnet_run = 8,
    directory_change = 9,
    directory_current = 10,
}
