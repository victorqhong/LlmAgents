using Azure.Monitor.OpenTelemetry.Exporter;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi.Content;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Diagnostics.Metrics;

namespace RLArena;

internal class Evaluate(
    AgentParameters agentParameters,
    string qTableFile,
    string? azureMonitorConnectionString,
    string runId)
{
    static string goal = "Create a dotnet console application in C# that writes 'hello world'.";
    static string systemPrompt = $"Goal: {goal}";

    CircularBuffer<Actions> actionBuffer = new CircularBuffer<Actions>(5);

    QTable qTable = new QTable();
    RLArena.Environment env;
    States state = States.BuildFail;

    LlmAgent? agent;
    ILogger? log;

    int episodes = 100;

    public async Task EvaluateRLAgent()
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
        var stepsPerEpisode = meter.CreateCounter<long>("stepsPerEpisode", "count");

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        });

        var consoleCommunication = new ConsoleCommunication()
        {
            NullOutput = false
        };

        log = loggerFactory.CreateLogger(nameof(Evaluate));

        agent = await LlmAgent.CreateAgent(loggerFactory, consoleCommunication,
            agentParameters.ApiEndpoint,
            agentParameters.ApiKey,
            agentParameters.ApiModel,
            agentParameters.ContextSize,
            agentParameters.MaxCompletionTokens,
            agentParameters.AgentId,
            agentParameters.WorkingDirectory,
            agentParameters.StorageDirectory,
            false,
            systemPrompt,
            null,
            agentParameters.ToolsFilePath,
            null);
        agent.StreamOutput = true;

        env = new RLArena.Environment(agent, agentParameters.WorkingDirectory);

        qTable.LoadQTable(qTableFile);

        var agentUseQTable = true;

        var timeStep = 0;
        for (int episode = 0; episode < episodes; episode++)
        {
            var episodeStart = DateTime.Now;
            var step = 0;

            env.ResetEnvironment();
            state = States.BuildFail;
            while (state != States.GoalComplete)
            {
                var stepStart = DateTime.Now;

                if (!agentUseQTable)
                {
                    await RunAgentAction(state);
                }
                else
                {
                    var action = BestAction(state, actionBuffer);
                    await RunAction(state, action);
                    actionBuffer.Add(action);
                }

                state = EvaluateState();

                var stepEnd = DateTime.Now;
                stepDuration.Add((stepEnd - stepStart).TotalMilliseconds, new KeyValuePair<string, object?>("episode", episode), new KeyValuePair<string, object?>("step", step), new KeyValuePair<string, object?>("time", timeStep));

                step++;
            }

            var episodeEnd = DateTime.Now;
            episodeDuration.Add((episodeEnd - episodeStart).TotalMilliseconds, new KeyValuePair<string, object?>("episode", episode), new KeyValuePair<string, object?>("step", step), new KeyValuePair<string, object?>("time", timeStep));

            stepsPerEpisode.Add(step, new KeyValuePair<string, object?>("episode", episode), new KeyValuePair<string, object?>("step", step), new KeyValuePair<string, object?>("time", timeStep));

            timeStep++;
        }
    }

    Actions BestAction(States state, CircularBuffer<Actions> buffer)
    {
        return (Actions)qTable.ArgMaxQ(state, buffer);
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
            toolChoice = "auto";
        }

        var content = new MessageContentText() { Text = $"{message}\nUse the {toolName} tool." };
        await agent.SendMessage([content], tools, toolChoice);
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
        return RunCommand("dotnet", "build", agentParameters.WorkingDirectory, out _);
    }

    bool FeatureIncomplete()
    {
        RunCommand("dotnet", "run", agentParameters.WorkingDirectory, out var stdout);
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