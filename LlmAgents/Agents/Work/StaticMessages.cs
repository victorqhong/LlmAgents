namespace LlmAgents.Agents.Work;

using Newtonsoft.Json.Linq;

public class StaticMessages : LlmAgentWork
{
    public StaticMessages(ICollection<JObject> staticMessages, LlmAgent agent)
        : base(agent)
    {
        Messages = staticMessages;
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public override Task Run(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

