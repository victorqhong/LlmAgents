using Azure.Monitor.OpenTelemetry.Exporter;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RLArena;

internal class Train(
    AgentParameters agentParameters,
    string qTableFile,
    string? azureMonitorConnectionString,
    string runId)
{
    static string goal = "The user cannot read messages and can only respond with the current state.\n\nCreate a dotnet console application in C# that writes 'hello world'.";
    static string systemPrompt = $"Goal: {goal}";

    string workingDirectory = agentParameters.WorkingDirectory;

    string[] states = Enum.GetNames<States>();
    string[] actions = Enum.GetNames<Actions>();

    QTable qTable = new QTable();
    RLArena.Environment env;
    States state = States.BuildFail;

    CircularBuffer<Actions> actionBuffer = new CircularBuffer<Actions>(5);

    double alpha = 0.1; // Learning rate
    double gamma = 0.9; // Discount factor
    double epsilon = 0.3; // Exploration rate
    Random rand = new();

    int episodes = 100;
    int episode = 0;
    int step = 0;

    ILogger? log;
    LlmAgent? agent;

    public async Task TrainAgent()
    {
        var resourceAttributes = new Dictionary<string, object>
            {
                { "service.name", "rlarena" },
                { "service.namespace", "evaluate" },
                { "service.instance.id", System.Environment.MachineName },
                { "service.version", runId },
            };

        var resourceBuilder = ResourceBuilder.CreateDefault().AddAttributes(resourceAttributes);

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddMeter("rlarena")
            .AddAzureMonitorMetricExporter(options => options.ConnectionString = azureMonitorConnectionString)
            .Build();

        var meter = new Meter("rlarena");
        var stepDuration = meter.CreateCounter<double>("stepDuration", "milliseconds");
        var episodeDuration = meter.CreateCounter<double>("episodeDuration", "milliseconds");
        var tokensGenerated = meter.CreateCounter<long>("tokensGenerated", "count");

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddOpenTelemetry(logging =>
                {
                    logging.AddAzureMonitorLogExporter(options =>
                    {
                        options.ConnectionString = azureMonitorConnectionString;
                    });
                });
        });

        log = loggerFactory.CreateLogger(nameof(Train));

        var consoleCommunication = new ConsoleCommunication()
        {
            NullOutput = true
        };

        agent = await LlmAgent.CreateAgent(loggerFactory, consoleCommunication, agentParameters.ApiEndpoint, agentParameters.ApiKey, agentParameters.ApiModel, agentParameters.ContextSize, agentParameters.MaxCompletionTokens, agentParameters.AgentId, workingDirectory, agentParameters.StorageDirectory, toolsFilePath: agentParameters.ToolsFilePath, systemPrompt: systemPrompt);
        agent.StreamOutput = true;

        agent.llmApi.PostParseUsage += usage =>
        {
            tokensGenerated.Add(usage.CompletionTokens, new KeyValuePair<string, object?>("episode", episode), new KeyValuePair<string, object?>("step", step));
        };

        agent.ToolEventBus?.SubscribeToolEvent<Tool>(async e =>
        {
            if (e.Sender.Name.Equals("directory_change"))
            {
                var tce = e as ToolCallEvent;
                var currentDirectory = tce.Result.Value<string>("currentDirectory");
                if (!string.IsNullOrEmpty(currentDirectory))
                {
                    workingDirectory = currentDirectory;
                }
            }

            if (env.ResettingEnvironment)
            {
                return;
            }

            if (state == States.GoalComplete)
            {
                return;
            }

            var toolName = e.Sender.Name;
            var action = Enum.Parse<Actions>(toolName);
            UpdateQTable(action);
        });

        env = new RLArena.Environment(agent, workingDirectory);

        bool loadResults = true;
        bool saveResults = true;
        bool agentMode = false;

        if (loadResults)
        {
            qTable.LoadQTable(qTableFile);
        }

        for (episode = 0; episode < episodes; episode++)
        {
            var episodeStart = DateTime.Now;
            state = States.BuildFail;

            Console.WriteLine($"======================== Episode {episode + 1} ========================");
            env.ResetEnvironment();
            step = 0;

            epsilon = (1.0 - episode / (float)(episodes - 1)) * 0.4 + 0.05;
            //epsilon = 0;

            actionBuffer = new CircularBuffer<Actions>(5); // Reset buffer each episode
            while (state != States.GoalComplete)
            {
                var stepStart = DateTime.Now;
                if (step >= 60)
                {
                    break;
                }

                if (agentMode)
                {
                    Console.WriteLine($"======================== Episode {episode + 1} Step {step + 1} ========================");
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
                        action = BestAction(state, actionBuffer);
                    }

                    Console.WriteLine($"======================== Episode {episode + 1} Step {step + 1} Action {action} ========================");
                    await RunAction(state, action);

                    if (action == Actions.think)
                    {
                        UpdateQTable(action);
                    }
                }

                step++;
                var stepEnd = DateTime.Now;
                stepDuration.Add((stepEnd - stepStart).TotalMilliseconds, new KeyValuePair<string, object?>("episode", episode), new KeyValuePair<string, object?>("step", step));
            }

            var episodeEnd = DateTime.Now;
            episodeDuration.Add((episodeEnd - episodeStart).TotalMilliseconds, new KeyValuePair<string, object?>("episode", episode), new KeyValuePair<string, object?>("step", step));

            log.LogInformation("episode: {episode}, steps: {steps}, epsilon: {epsilon}, goal complete: {goalComplete}", episode, step, epsilon, state == States.GoalComplete);

            agent.Messages.Clear();
            agent.Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));

            if (saveResults)
            {
                qTable.SaveQTable(qTableFile);
            }
        }

        qTable.SaveQTable(qTableFile);
        qTable.LoadQTable(qTableFile);

        Debugger.Launch();
    }


    void UpdateQTable(Actions action)
    {
        var newState = EvaluateState();
        var reward = CalculateReward(newState, state, action, actionBuffer);
        log.LogInformation("reward: {reward}, action: {action}, state: {state}, newState: {newState}, episode: {episode}, step: {step}", reward, action, state, newState, episode, step);
        var (qVals, key) = qTable.GetQTableEntry(state, actionBuffer);
        actionBuffer.Add(action);
        qVals[(int)action] += alpha * (reward + gamma * qTable.MaxQ(newState, actionBuffer) - qVals[(int)action]);
        state = newState;
    }

    Actions BestAction(States state, CircularBuffer<Actions> buffer)
    {
        return (Actions)qTable.ArgMaxQ(state, buffer);
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

        var toolName = Enum.GetName<Actions>(action);
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
            toolChoice = "required";
        }

        var content = new MessageContentText() { Text = $"{message}\nUse the {toolName} tool." };
        await agent.SendMessage([content], tools, toolChoice);
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
        else if (newState == States.BuildFail && oldState == States.GoalIncomplete)
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

        if (newState == States.GoalIncomplete && action == Actions.dotnet_run)
        {
            return 0.2;
        }
        else if (newState == States.BuildFail && action == Actions.dotnet_run)
        {
            return -0.2;
        }
        else if (newState == States.BuildFail && action == Actions.dotnet_build)
        {
            return 0.2;
        }
        else if (newState == States.BuildFail && action == Actions.file_write)
        {
            return 0.2;
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
            workingDirectory = System.Environment.CurrentDirectory;
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
}