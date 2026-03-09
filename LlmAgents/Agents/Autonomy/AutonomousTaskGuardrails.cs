namespace LlmAgents.Agents.Autonomy;

public sealed class AutonomousTaskGuardrails
{
    private static readonly TaskPolicy DefaultPolicy = new();

    public TaskPolicy Policy { get; }
    public DateTime StartedAtUtc { get; }
    public DateTime DeadlineUtc { get; }
    public int ToolCallCycles { get; private set; }
    public int TotalTokens { get; private set; }

    public AutonomousTaskGuardrails(TaskPolicy? policy, DateTime? startedAtUtc = null)
    {
        Policy = NormalizePolicy(policy);
        StartedAtUtc = startedAtUtc ?? DateTime.UtcNow;
        DeadlineUtc = StartedAtUtc.AddMinutes(Policy.MaxRuntimeMinutes);
    }

    public void RecordToolCallCycle()
    {
        ToolCallCycles += 1;
    }

    public bool IsToolCallBudgetExceeded()
    {
        return ToolCallCycles > Policy.MaxToolCalls;
    }

    public void RecordTokenUsage(int tokens)
    {
        if (tokens > 0)
        {
            TotalTokens += tokens;
        }
    }

    public bool IsTokenBudgetExceeded()
    {
        return TotalTokens > Policy.MaxTokens;
    }

    public bool IsDeadlineExceeded(DateTime? nowUtc = null)
    {
        return (nowUtc ?? DateTime.UtcNow) > DeadlineUtc;
    }

    public static TimeSpan GetRetryBackoffDelay(int retryCount)
    {
        if (retryCount <= 1)
        {
            return TimeSpan.FromSeconds(1);
        }

        var exponent = Math.Min(5, retryCount - 1);
        var seconds = (int)Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(seconds);
    }

    public static TaskPolicy NormalizePolicy(TaskPolicy? policy)
    {
        return new TaskPolicy
        {
            MaxRetriesPerStep = policy?.MaxRetriesPerStep > 0 ? policy.MaxRetriesPerStep : DefaultPolicy.MaxRetriesPerStep,
            MaxToolCalls = policy?.MaxToolCalls > 0 ? policy.MaxToolCalls : DefaultPolicy.MaxToolCalls,
            MaxTokens = policy?.MaxTokens > 0 ? policy.MaxTokens : DefaultPolicy.MaxTokens,
            MaxRuntimeMinutes = policy?.MaxRuntimeMinutes > 0 ? policy.MaxRuntimeMinutes : DefaultPolicy.MaxRuntimeMinutes
        };
    }
}
