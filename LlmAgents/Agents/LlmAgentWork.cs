namespace LlmAgents.Agents;

using Newtonsoft.Json.Linq;

public abstract class LlmAgentWork<T> : ILlmAgentWork
{
    public abstract Task<T?> Work(CancellationToken ct);

    public abstract Task OnCompleted(T? result, CancellationToken ct);

    public abstract Task<ICollection<JObject>?> GetState(CancellationToken ct);

    public abstract ICollection<JObject>? Messages { get; protected set; }

    public T? WorkResult { get; private set; }

    public readonly LlmAgent agent;

    public LlmAgentWork(LlmAgent agent)
    {
        this.agent = agent;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await Work(cancellationToken);
        WorkResult = result;
        await OnCompleted(result, cancellationToken);
    }
}
