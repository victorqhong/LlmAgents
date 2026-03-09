using LlmAgents.Agents.Autonomy;
using LlmAgents.State;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToolServer;

internal abstract class AutonomyControlPlaneTool : McpServerTool
{
    protected readonly ILoggerFactory LoggerFactory;
    private readonly string storageDirectory;
    private readonly string defaultAgentId;
    private readonly Tool protocolTool;

    protected AutonomyControlPlaneTool(
        ILoggerFactory loggerFactory,
        string storageDirectory,
        string defaultAgentId,
        string toolName,
        string description,
        string inputSchemaJson)
    {
        LoggerFactory = loggerFactory;
        this.storageDirectory = storageDirectory;
        this.defaultAgentId = defaultAgentId;

        using var schemaDocument = JsonDocument.Parse(inputSchemaJson);
        protocolTool = new Tool
        {
            Name = toolName,
            Description = description,
            InputSchema = schemaDocument.RootElement.Clone()
        };
    }

    public override Tool ProtocolTool => protocolTool;

    protected string ResolveAgentId(RequestContext<CallToolRequestParams> request, JObject arguments)
    {
        var agentId = arguments.Value<string>("agentId");
        if (!string.IsNullOrWhiteSpace(agentId))
        {
            return agentId;
        }

        var headerAgentId = request.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers["X-Agent-Id"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(headerAgentId) ? defaultAgentId : headerAgentId;
    }

    protected static string? ResolveSessionId(RequestContext<CallToolRequestParams> request, JObject arguments)
    {
        var sessionId = arguments.Value<string>("sessionId");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return sessionId;
        }

        var headerSessionId = request.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers["X-Session-Id"].FirstOrDefault();
        return string.IsNullOrWhiteSpace(headerSessionId) ? null : headerSessionId;
    }

    protected StateDatabase CreateStateDatabase(string agentId)
    {
        Directory.CreateDirectory(storageDirectory);
        var databasePath = Path.Combine(storageDirectory, $"{SanitizeAgentId(agentId)}.db");
        return new StateDatabase(LoggerFactory, databasePath);
    }

    protected static JObject ParseArguments(RequestContext<CallToolRequestParams> request)
    {
        if (request.Params?.Arguments == null)
        {
            return new JObject();
        }

        return JObject.Parse(JsonSerializer.Serialize(request.Params.Arguments));
    }

    protected static JObject BuildTaskJson(TaskInstance task, bool includeSteps)
    {
        var orderedSteps = task.Steps.OrderBy(s => s.Sequence).ToList();
        var doneSteps = orderedSteps.Count(step => step.State == TaskStepState.Done);
        var runningSteps = orderedSteps.Count(step => step.State == TaskStepState.Running);
        var waitingSteps = orderedSteps.Count(step => step.State == TaskStepState.Waiting);
        var failedSteps = orderedSteps.Count(step => step.State == TaskStepState.Failed);
        var cancelledSteps = orderedSteps.Count(step => step.State == TaskStepState.Cancelled);
        var pendingSteps = orderedSteps.Count(step => step.State == TaskStepState.Pending);
        var totalSteps = orderedSteps.Count;
        if (totalSteps == 0 && task.State == TaskState.Completed)
        {
            totalSteps = 1;
            doneSteps = 1;
        }

        var progressPercent = totalSteps == 0 ? 0 : Math.Round((double)doneSteps * 100 / totalSteps, 2);
        var taskJson = new JObject
        {
            ["id"] = task.Id,
            ["agent_id"] = task.AgentId,
            ["session_id"] = task.SessionId ?? string.Empty,
            ["conversation_id"] = task.ConversationId ?? string.Empty,
            ["goal"] = task.Goal,
            ["status"] = task.State.ToString().ToLowerInvariant(),
            ["priority"] = task.Priority,
            ["current_step_id"] = task.CurrentStepId ?? string.Empty,
            ["result_summary"] = task.ResultSummary ?? string.Empty,
            ["last_error"] = task.LastError ?? string.Empty,
            ["created_at"] = task.CreatedAt.ToString("O"),
            ["updated_at"] = task.UpdatedAt.ToString("O"),
            ["progress"] = new JObject
            {
                ["total_steps"] = totalSteps,
                ["done_steps"] = doneSteps,
                ["running_steps"] = runningSteps,
                ["waiting_steps"] = waitingSteps,
                ["failed_steps"] = failedSteps,
                ["cancelled_steps"] = cancelledSteps,
                ["pending_steps"] = pendingSteps,
                ["percent"] = progressPercent
            }
        };

        if (includeSteps)
        {
            taskJson["steps"] = new JArray(orderedSteps.Select(step => new JObject
            {
                ["id"] = step.Id,
                ["sequence"] = step.Sequence,
                ["title"] = step.Title,
                ["kind"] = step.Kind,
                ["status"] = step.State.ToString().ToLowerInvariant(),
                ["retry_count"] = step.RetryCount,
                ["last_error"] = step.LastError ?? string.Empty,
                ["updated_at"] = step.UpdatedAt.ToString("O")
            }));
        }

        return taskJson;
    }

    protected static CallToolResult Success(JObject payload)
    {
        return new CallToolResult
        {
            StructuredContent = JsonNode.Parse(payload.ToString()) ?? new JsonObject()
        };
    }

    protected static CallToolResult Error(string message)
    {
        var result = new CallToolResult { IsError = true };
        result.Content.Add(new TextContentBlock { Text = message });
        return result;
    }

    private static string SanitizeAgentId(string agentId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeId = new string(agentId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(safeId) ? Environment.MachineName : safeId;
    }
}

internal sealed class AutonomyTaskSubmitTool : AutonomyControlPlaneTool
{
    public AutonomyTaskSubmitTool(ILoggerFactory loggerFactory, string storageDirectory, string defaultAgentId)
        : base(
            loggerFactory,
            storageDirectory,
            defaultAgentId,
            "autonomy_task_submit",
            "Submit a new autonomous task",
            """
            {
              "type": "object",
              "properties": {
                "goal": { "type": "string", "description": "High-level goal for autonomous execution" },
                "agentId": { "type": "string", "description": "Optional agent identifier; defaults to X-Agent-Id header or server default" },
                "sessionId": { "type": "string", "description": "Optional session identifier; defaults to X-Session-Id header when available" },
                "conversationId": { "type": "string", "description": "Optional conversation identifier" },
                "policy": { "type": "object", "description": "Optional task policy overrides" }
              },
              "required": ["goal"],
              "additionalProperties": false
            }
            """)
    {
    }

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var arguments = ParseArguments(request);
        var goal = arguments.Value<string>("goal");
        if (string.IsNullOrWhiteSpace(goal))
        {
            return ValueTask.FromResult(Error("goal is required"));
        }

        var agentId = ResolveAgentId(request, arguments);
        var sessionId = ResolveSessionId(request, arguments);
        var conversationId = arguments.Value<string>("conversationId");
        var policy = arguments["policy"]?.ToObject<TaskPolicy>();

        using var stateDatabase = CreateStateDatabase(agentId);
        var taskStore = new AutonomousTaskStore(LoggerFactory, stateDatabase);
        var coordinator = new AutonomyCoordinator(taskStore);
        var task = coordinator.EnqueueTaskFromUserInput(goal, agentId, sessionId, conversationId, policy);

        return ValueTask.FromResult(Success(new JObject
        {
            ["task"] = BuildTaskJson(task, includeSteps: true)
        }));
    }
}

internal sealed class AutonomyTaskStatusTool : AutonomyControlPlaneTool
{
    public AutonomyTaskStatusTool(ILoggerFactory loggerFactory, string storageDirectory, string defaultAgentId)
        : base(
            loggerFactory,
            storageDirectory,
            defaultAgentId,
            "autonomy_task_status",
            "Get status and progress for an autonomous task",
            """
            {
              "type": "object",
              "properties": {
                "taskId": { "type": "string", "description": "Task identifier" },
                "agentId": { "type": "string", "description": "Optional agent identifier for selecting the autonomy store database" },
                "includeEvents": { "type": "boolean", "description": "Whether to include recent task events", "default": true },
                "eventLimit": { "type": "integer", "description": "Maximum number of events to return", "minimum": 1, "maximum": 200, "default": 20 }
              },
              "required": ["taskId"],
              "additionalProperties": false
            }
            """)
    {
    }

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var arguments = ParseArguments(request);
        var taskId = arguments.Value<string>("taskId");
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return ValueTask.FromResult(Error("taskId is required"));
        }

        var agentId = ResolveAgentId(request, arguments);
        using var stateDatabase = CreateStateDatabase(agentId);
        var taskStore = new AutonomousTaskStore(LoggerFactory, stateDatabase);
        var task = taskStore.GetTask(taskId);
        if (task == null)
        {
            return ValueTask.FromResult(Error($"Task not found: {taskId}"));
        }

        var response = new JObject
        {
            ["task"] = BuildTaskJson(task, includeSteps: true)
        };

        var includeEvents = arguments.Value<bool?>("includeEvents") ?? true;
        if (includeEvents)
        {
            var eventLimit = Math.Clamp(arguments.Value<int?>("eventLimit") ?? 20, 1, 200);
            response["events"] = new JArray(taskStore.ListTaskEvents(taskId, eventLimit).Select(taskEvent => new JObject
            {
                ["id"] = taskEvent.Id,
                ["event_type"] = taskEvent.EventType,
                ["message"] = taskEvent.Message,
                ["data_json"] = taskEvent.DataJson ?? string.Empty,
                ["created_at"] = taskEvent.CreatedAt.ToString("O")
            }));
        }

        return ValueTask.FromResult(Success(response));
    }
}

internal sealed class AutonomyTaskListTool : AutonomyControlPlaneTool
{
    public AutonomyTaskListTool(ILoggerFactory loggerFactory, string storageDirectory, string defaultAgentId)
        : base(
            loggerFactory,
            storageDirectory,
            defaultAgentId,
            "autonomy_task_list",
            "List autonomous tasks",
            """
            {
              "type": "object",
              "properties": {
                "status": { "type": "string", "description": "Optional task status filter" },
                "limit": { "type": "integer", "description": "Maximum number of tasks to return", "minimum": 1, "maximum": 200, "default": 50 },
                "agentId": { "type": "string", "description": "Optional agent identifier for selecting the autonomy store database" }
              },
              "additionalProperties": false
            }
            """)
    {
    }

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var arguments = ParseArguments(request);
        var status = arguments.Value<string>("status");
        var limit = Math.Clamp(arguments.Value<int?>("limit") ?? 50, 1, 200);
        var agentId = ResolveAgentId(request, arguments);

        using var stateDatabase = CreateStateDatabase(agentId);
        var taskStore = new AutonomousTaskStore(LoggerFactory, stateDatabase);
        var tasks = taskStore.ListTasks(status, limit);

        return ValueTask.FromResult(Success(new JObject
        {
            ["tasks"] = new JArray(tasks.Select(task => BuildTaskJson(task, includeSteps: false)))
        }));
    }
}

internal sealed class AutonomyTaskResumeTool : AutonomyControlPlaneTool
{
    public AutonomyTaskResumeTool(ILoggerFactory loggerFactory, string storageDirectory, string defaultAgentId)
        : base(
            loggerFactory,
            storageDirectory,
            defaultAgentId,
            "autonomy_task_resume",
            "Resume an autonomous task",
            """
            {
              "type": "object",
              "properties": {
                "taskId": { "type": "string", "description": "Task identifier" },
                "agentId": { "type": "string", "description": "Optional agent identifier for selecting the autonomy store database" }
              },
              "required": ["taskId"],
              "additionalProperties": false
            }
            """)
    {
    }

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var arguments = ParseArguments(request);
        var taskId = arguments.Value<string>("taskId");
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return ValueTask.FromResult(Error("taskId is required"));
        }

        var agentId = ResolveAgentId(request, arguments);
        using var stateDatabase = CreateStateDatabase(agentId);
        var taskStore = new AutonomousTaskStore(LoggerFactory, stateDatabase);
        if (!taskStore.ResumeTask(taskId))
        {
            return ValueTask.FromResult(Error($"Task not found: {taskId}"));
        }

        var task = taskStore.GetTask(taskId);
        return ValueTask.FromResult(Success(new JObject
        {
            ["task"] = task == null ? new JObject() : BuildTaskJson(task, includeSteps: true)
        }));
    }
}

internal sealed class AutonomyTaskCancelTool : AutonomyControlPlaneTool
{
    public AutonomyTaskCancelTool(ILoggerFactory loggerFactory, string storageDirectory, string defaultAgentId)
        : base(
            loggerFactory,
            storageDirectory,
            defaultAgentId,
            "autonomy_task_cancel",
            "Cancel an autonomous task",
            """
            {
              "type": "object",
              "properties": {
                "taskId": { "type": "string", "description": "Task identifier" },
                "agentId": { "type": "string", "description": "Optional agent identifier for selecting the autonomy store database" }
              },
              "required": ["taskId"],
              "additionalProperties": false
            }
            """)
    {
    }

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var arguments = ParseArguments(request);
        var taskId = arguments.Value<string>("taskId");
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return ValueTask.FromResult(Error("taskId is required"));
        }

        var agentId = ResolveAgentId(request, arguments);
        using var stateDatabase = CreateStateDatabase(agentId);
        var taskStore = new AutonomousTaskStore(LoggerFactory, stateDatabase);
        if (!taskStore.CancelTask(taskId))
        {
            return ValueTask.FromResult(Error($"Task not found: {taskId}"));
        }

        var task = taskStore.GetTask(taskId);
        return ValueTask.FromResult(Success(new JObject
        {
            ["task"] = task == null ? new JObject() : BuildTaskJson(task, includeSteps: true)
        }));
    }
}
