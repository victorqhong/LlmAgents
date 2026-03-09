using LlmAgents.Agents;
using LlmAgents.Agents.Autonomy;
using LlmAgents.CommandLineParser;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using LlmAgents.State;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.CommandLine;

namespace ConsoleAgent.Commands;

internal class TaskCommand : Command
{
    private readonly ILoggerFactory loggerFactory;

    private readonly Option<string> agentIdOption = new("--agentId")
    {
        Description = "Agent identifier used for autonomous tasks",
        DefaultValueFactory = _ => Environment.MachineName
    };

    private readonly Option<string> storageDirectoryOption = new("--storageDirectory")
    {
        Description = "Directory for task and agent storage",
        DefaultValueFactory = _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlmAgents")
    };

    private readonly Option<string?> sessionIdOption = new("--sessionId")
    {
        Description = "Optional session id associated with tasks",
        DefaultValueFactory = _ => Config.GetConfigOptionDefaultValue(".llmagents-session", "LLMAGENTS_SESSION")
    };

    private readonly Option<string?> apiConfigOption = new("--apiConfig")
    {
        Description = "Path to API config JSON",
        DefaultValueFactory = _ => Config.GetConfigOptionDefaultValue("api.json", "LLMAGENTS_API_CONFIG")
    };

    private readonly Option<string?> apiEndpointOption = new("--apiEndpoint")
    {
        Description = "OpenAI-compatible API endpoint"
    };

    private readonly Option<string?> apiKeyOption = new("--apiKey")
    {
        Description = "API key"
    };

    private readonly Option<string?> apiModelOption = new("--apiModel")
    {
        Description = "Model name"
    };

    private readonly Option<int> contextSizeOption = new("--contextSize")
    {
        Description = "Context size",
        DefaultValueFactory = _ => 8192
    };

    private readonly Option<int> maxCompletionTokensOption = new("--maxCompletionTokens")
    {
        Description = "Maximum completion tokens",
        DefaultValueFactory = _ => 8192
    };

    private readonly Option<string?> toolsConfigOption = new("--toolsConfig")
    {
        Description = "Path to tools config JSON",
        DefaultValueFactory = _ => Config.GetConfigOptionDefaultValue("tools.json", "LLMAGENTS_TOOLS_CONFIG")
    };

    private readonly Option<string?> mcpConfigPathOption = new("--mcpConfigPath")
    {
        Description = "Path to MCP config JSON",
        DefaultValueFactory = _ => Config.GetConfigOptionDefaultValue("mcp.json", "LLMAGENTS_MCP_CONFIG")
    };

    private readonly Option<string> workingDirectoryOption = new("--workingDirectory")
    {
        Description = "Working directory for tool execution",
        DefaultValueFactory = _ => Environment.CurrentDirectory
    };

    private readonly Option<string> systemPromptFileOption = new("--systemPromptFile")
    {
        Description = "Optional system prompt file path",
        DefaultValueFactory = _ => string.Empty
    };

    private readonly Option<bool> streamOutputOption = new("--streamOutput")
    {
        Description = "Whether assistant output is streamed during autonomous execution",
        DefaultValueFactory = _ => true
    };

    private readonly Argument<string> taskIdArgument = new("taskId")
    {
        Description = "Task identifier"
    };

    private readonly Argument<string> goalArgument = new("goal")
    {
        Description = "High-level goal for autonomous execution"
    };

    public TaskCommand(ILoggerFactory loggerFactory)
        : base("task", "Autonomous task operations")
    {
        this.loggerFactory = loggerFactory;

        Options.Add(agentIdOption);
        Options.Add(storageDirectoryOption);
        Options.Add(sessionIdOption);
        Options.Add(apiConfigOption);
        Options.Add(apiEndpointOption);
        Options.Add(apiKeyOption);
        Options.Add(apiModelOption);
        Options.Add(contextSizeOption);
        Options.Add(maxCompletionTokensOption);
        Options.Add(toolsConfigOption);
        Options.Add(mcpConfigPathOption);
        Options.Add(workingDirectoryOption);
        Options.Add(systemPromptFileOption);
        Options.Add(streamOutputOption);

        var submitCommand = new Command("submit", "Submit a new autonomous task");
        submitCommand.Arguments.Add(goalArgument);
        submitCommand.SetAction(SubmitTask);
        Add(submitCommand);

        var statusCommand = new Command("status", "Get status of a task");
        statusCommand.Arguments.Add(taskIdArgument);
        statusCommand.SetAction(TaskStatus);
        Add(statusCommand);

        var listStatusOption = new Option<string?>("--status")
        {
            Description = "Optional status filter (pending|running|waiting|completed|failed|cancelled)"
        };
        var listCommand = new Command("list", "List tasks");
        listCommand.Options.Add(listStatusOption);
        listCommand.SetAction((ParseResult parseResult) => ListTasks(parseResult, listStatusOption));
        Add(listCommand);

        var resumeCommand = new Command("resume", "Resume a task by setting it back to pending");
        resumeCommand.Arguments.Add(taskIdArgument);
        resumeCommand.SetAction(ResumeTask);
        Add(resumeCommand);

        var cancelCommand = new Command("cancel", "Cancel a task");
        cancelCommand.Arguments.Add(taskIdArgument);
        cancelCommand.SetAction(CancelTask);
        Add(cancelCommand);

        var pollSecondsOption = new Option<int>("--pollSeconds")
        {
            Description = "Task queue polling interval in seconds",
            DefaultValueFactory = _ => 2
        };
        var runnerCommand = new Command("runner", "Run the autonomous task runner loop");
        runnerCommand.Options.Add(pollSecondsOption);
        runnerCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) => RunRunner(parseResult, pollSecondsOption, cancellationToken));
        Add(runnerCommand);
    }

    private void SubmitTask(ParseResult parseResult)
    {
        var goal = parseResult.GetValue(goalArgument);
        if (string.IsNullOrWhiteSpace(goal))
        {
            Console.Error.WriteLine("goal is required");
            return;
        }

        var storageDirectory = EnsureStorageDirectory(parseResult);
        var agentId = parseResult.GetValue(agentIdOption) ?? Environment.MachineName;
        var sessionId = parseResult.GetValue(sessionIdOption);

        using var stateDatabase = CreateStateDatabase(storageDirectory, agentId);
        var taskStore = new AutonomousTaskStore(loggerFactory, stateDatabase);
        var coordinator = new AutonomyCoordinator(taskStore);
        var task = coordinator.EnqueueTaskFromUserInput(goal, agentId, sessionId);

        Console.WriteLine($"task_id: {task.Id}");
        Console.WriteLine($"status: {task.State.ToString().ToLowerInvariant()}");
    }

    private void TaskStatus(ParseResult parseResult)
    {
        var taskId = parseResult.GetValue(taskIdArgument);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            Console.Error.WriteLine("taskId is required");
            return;
        }

        var storageDirectory = EnsureStorageDirectory(parseResult);
        var agentId = parseResult.GetValue(agentIdOption) ?? Environment.MachineName;

        using var stateDatabase = CreateStateDatabase(storageDirectory, agentId);
        var taskStore = new AutonomousTaskStore(loggerFactory, stateDatabase);
        var task = taskStore.GetTask(taskId);
        if (task == null)
        {
            Console.WriteLine($"Task not found: {taskId}");
            return;
        }

        Console.WriteLine($"id: {task.Id}");
        Console.WriteLine($"status: {task.State.ToString().ToLowerInvariant()}");
        Console.WriteLine($"agent_id: {task.AgentId}");
        Console.WriteLine($"created_at: {task.CreatedAt:O}");
        Console.WriteLine($"updated_at: {task.UpdatedAt:O}");
        if (!string.IsNullOrEmpty(task.LastError))
        {
            Console.WriteLine($"last_error: {task.LastError}");
        }

        if (task.Steps.Count > 0)
        {
            Console.WriteLine("steps:");
            foreach (var step in task.Steps.OrderBy(s => s.Sequence))
            {
                Console.WriteLine($"  - [{step.State.ToString().ToLowerInvariant()}] {step.Sequence}: {step.Title} ({step.Kind})");
            }
        }
    }

    private void ListTasks(ParseResult parseResult, Option<string?> statusOption)
    {
        var status = parseResult.GetValue(statusOption);
        var storageDirectory = EnsureStorageDirectory(parseResult);
        var agentId = parseResult.GetValue(agentIdOption) ?? Environment.MachineName;

        using var stateDatabase = CreateStateDatabase(storageDirectory, agentId);
        var taskStore = new AutonomousTaskStore(loggerFactory, stateDatabase);
        var tasks = taskStore.ListTasks(status, 100);
        if (tasks.Count == 0)
        {
            Console.WriteLine("No tasks");
            return;
        }

        foreach (var task in tasks)
        {
            Console.WriteLine($"{task.Id} [{task.State.ToString().ToLowerInvariant()}] {task.Goal}");
        }
    }

    private void ResumeTask(ParseResult parseResult)
    {
        var taskId = parseResult.GetValue(taskIdArgument);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            Console.Error.WriteLine("taskId is required");
            return;
        }

        var storageDirectory = EnsureStorageDirectory(parseResult);
        var agentId = parseResult.GetValue(agentIdOption) ?? Environment.MachineName;

        using var stateDatabase = CreateStateDatabase(storageDirectory, agentId);
        var taskStore = new AutonomousTaskStore(loggerFactory, stateDatabase);
        taskStore.ResumeTask(taskId);
        Console.WriteLine($"Task resumed: {taskId}");
    }

    private void CancelTask(ParseResult parseResult)
    {
        var taskId = parseResult.GetValue(taskIdArgument);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            Console.Error.WriteLine("taskId is required");
            return;
        }

        var storageDirectory = EnsureStorageDirectory(parseResult);
        var agentId = parseResult.GetValue(agentIdOption) ?? Environment.MachineName;

        using var stateDatabase = CreateStateDatabase(storageDirectory, agentId);
        var taskStore = new AutonomousTaskStore(loggerFactory, stateDatabase);
        taskStore.CancelTask(taskId);
        Console.WriteLine($"Task cancelled: {taskId}");
    }

    private async Task RunRunner(ParseResult parseResult, Option<int> pollSecondsOption, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(nameof(TaskCommand));
        var apiParameters = ParseApiParameters(parseResult);
        if (apiParameters == null)
        {
            Console.Error.WriteLine("apiEndpoint/apiKey/apiModel (or apiConfig) is required for runner.");
            return;
        }

        var storageDirectory = EnsureStorageDirectory(parseResult);
        var agentId = parseResult.GetValue(agentIdOption) ?? Environment.MachineName;

        var workingDirectory = parseResult.GetValue(workingDirectoryOption);
        var systemPromptFile = parseResult.GetValue(systemPromptFileOption);
        var sessionId = parseResult.GetValue(sessionIdOption);

        var toolParameters = new ToolParameters
        {
            ToolsConfig = parseResult.GetValue(toolsConfigOption),
            McpConfigPath = parseResult.GetValue(mcpConfigPathOption)
        };

        var sessionParameters = new SessionParameters
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectory,
            SystemPromptFile = systemPromptFile
        };

        var agentParameters = new LlmAgentParameters
        {
            AgentId = agentId,
            Persistent = false,
            StorageDirectory = storageDirectory,
            StreamOutput = parseResult.GetValue(streamOutputOption)
        };

        using var stateDatabase = CreateStateDatabase(storageDirectory, agentId);
        var taskStore = new AutonomousTaskStore(loggerFactory, stateDatabase);

        var consoleCommunication = new ConsoleCommunication();
        var pollSeconds = Math.Max(1, parseResult.GetValue(pollSecondsOption));
        var runner = new AutonomousTaskRunner(
            loggerFactory,
            taskStore,
            consoleCommunication,
            apiParameters,
            agentParameters,
            toolParameters,
            sessionParameters);

        logger.LogInformation("Starting autonomous task runner with polling interval {seconds}s", pollSeconds);
        await runner.Run(cancellationToken, TimeSpan.FromSeconds(pollSeconds));
    }

    private LlmApiOpenAiParameters? ParseApiParameters(ParseResult parseResult)
    {
        string? apiEndpoint;
        string? apiKey;
        string? apiModel;
        var contextSize = parseResult.GetValue(contextSizeOption);
        var maxCompletionTokens = parseResult.GetValue(maxCompletionTokensOption);

        var apiConfigPath = parseResult.GetValue(apiConfigOption);
        if (!string.IsNullOrEmpty(apiConfigPath) && File.Exists(apiConfigPath))
        {
            var apiConfig = JObject.Parse(File.ReadAllText(apiConfigPath));
            apiEndpoint = apiConfig.Value<string>("apiEndpoint");
            apiKey = apiConfig.Value<string>("apiKey");
            apiModel = apiConfig.Value<string>("apiModel");
            if (apiConfig.Value<int?>("contextSize") is int configuredContextSize)
            {
                contextSize = configuredContextSize;
            }

            if (apiConfig.Value<int?>("maxCompletionTokens") is int configuredMaxCompletionTokens)
            {
                maxCompletionTokens = configuredMaxCompletionTokens;
            }
        }
        else
        {
            apiEndpoint = parseResult.GetValue(apiEndpointOption);
            apiKey = parseResult.GetValue(apiKeyOption);
            apiModel = parseResult.GetValue(apiModelOption);
        }

        if (string.IsNullOrEmpty(apiEndpoint) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiModel))
        {
            return null;
        }

        return new LlmApiOpenAiParameters
        {
            ApiEndpoint = apiEndpoint,
            ApiKey = apiKey,
            ApiModel = apiModel,
            ContextSize = contextSize,
            MaxCompletionTokens = maxCompletionTokens
        };
    }

    private string EnsureStorageDirectory(ParseResult parseResult)
    {
        var storageDirectory = parseResult.GetValue(storageDirectoryOption) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LlmAgents");
        if (!Path.Exists(storageDirectory))
        {
            Directory.CreateDirectory(storageDirectory);
        }

        return storageDirectory;
    }

    private StateDatabase CreateStateDatabase(string storageDirectory, string agentId)
    {
        var databasePath = Path.Combine(storageDirectory, $"{agentId}.db");
        return new StateDatabase(loggerFactory, databasePath);
    }
}
