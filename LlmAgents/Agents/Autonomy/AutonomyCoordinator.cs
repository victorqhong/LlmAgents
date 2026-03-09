namespace LlmAgents.Agents.Autonomy;

public class AutonomyCoordinator
{
    private readonly AutonomousTaskStore taskStore;

    public AutonomyCoordinator(AutonomousTaskStore taskStore)
    {
        this.taskStore = taskStore;
    }

    public virtual bool ShouldUseAutonomousExecution(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return false;
        }

        // Lightweight heuristic for now. This can be replaced with classifier logic.
        return userInput.Length > 120
            || userInput.Contains("implement", StringComparison.OrdinalIgnoreCase)
            || userInput.Contains("refactor", StringComparison.OrdinalIgnoreCase)
            || userInput.Contains("autonomous", StringComparison.OrdinalIgnoreCase)
            || userInput.Contains("minutes", StringComparison.OrdinalIgnoreCase)
            || userInput.Contains("hours", StringComparison.OrdinalIgnoreCase);
    }

    public TaskInstance EnqueueTaskFromUserInput(string goal, string agentId, string? sessionId = null, string? conversationId = null, TaskPolicy? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var task = new TaskInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            AgentId = agentId,
            SessionId = sessionId,
            ConversationId = conversationId,
            Goal = goal,
            State = TaskState.Pending,
            Priority = 10,
            Policy = policy ?? new TaskPolicy(),
            Steps =
            [
                new TaskStep { Id = Guid.NewGuid().ToString("N"), Sequence = 1, Title = "Plan work", Kind = "plan", State = TaskStepState.Pending },
                new TaskStep { Id = Guid.NewGuid().ToString("N"), Sequence = 2, Title = "Execute tool-driven work", Kind = "execute", State = TaskStepState.Pending },
                new TaskStep { Id = Guid.NewGuid().ToString("N"), Sequence = 3, Title = "Verify and summarize", Kind = "verify", State = TaskStepState.Pending }
            ]
        };

        taskStore.CreateTask(task);
        taskStore.AppendEvent(task.Id, "task_enqueued", "Task enqueued from user input");
        return task;
    }
}
