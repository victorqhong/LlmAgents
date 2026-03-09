namespace LlmAgents.Agents.Autonomy;

public enum TaskState
{
    Pending,
    Running,
    Waiting,
    Completed,
    Failed,
    Cancelled
}

public enum TaskStepState
{
    Pending,
    Running,
    Waiting,
    Done,
    Failed,
    Cancelled
}

public class TaskPolicy
{
    public int MaxRetriesPerStep { get; set; } = 3;
    public int MaxToolCalls { get; set; } = 200;
    public int MaxTokens { get; set; } = 200000;
    public int MaxRuntimeMinutes { get; set; } = 240;
}

public class TaskStep
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Kind { get; set; }
    public TaskStepState State { get; set; } = TaskStepState.Pending;
    public int RetryCount { get; set; }
    public int Sequence { get; set; }
    public string? PayloadJson { get; set; }
    public string? LastError { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TaskInstance
{
    public required string Id { get; set; }
    public required string AgentId { get; set; }
    public required string Goal { get; set; }
    public string? SessionId { get; set; }
    public string? ConversationId { get; set; }
    public TaskState State { get; set; } = TaskState.Pending;
    public int Priority { get; set; } = 10;
    public string? CurrentStepId { get; set; }
    public string? CheckpointJson { get; set; }
    public string? ResultSummary { get; set; }
    public string? LastError { get; set; }
    public TaskPolicy Policy { get; set; } = new();
    public List<TaskStep> Steps { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TaskEvent
{
    public long Id { get; set; }
    public required string TaskId { get; set; }
    public required string EventType { get; set; }
    public required string Message { get; set; }
    public string? DataJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
