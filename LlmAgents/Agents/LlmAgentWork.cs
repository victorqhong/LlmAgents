namespace LlmAgents.Agents;

using Newtonsoft.Json.Linq;

public abstract class LlmAgentWork : ILlmAgentWork
{
    public abstract Task<ICollection<JObject>?> GetState(CancellationToken ct);

    public ICollection<JObject>? Messages { get; protected set; }

    public readonly LlmAgent agent;

    public LlmAgentWork(LlmAgent agent)
    {
        this.agent = agent;
    }

    public abstract Task Run(CancellationToken cancellationToken);
}
