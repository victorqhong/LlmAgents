using Newtonsoft.Json.Linq;

namespace LlmAgents.Agents.Work;

public class CountingWork : LlmAgentWork<ICollection<JObject>>
{
    private volatile int index;

    public CountingWork(LlmAgent agent)
        : base(agent)
    {
    }

    public override ICollection<JObject>? Messages { get; protected set; } 

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>([JObject.FromObject(new { role = "assistant", content = $"i've counted to {index}" })]);
    }

    public override Task OnCompleted(ICollection<JObject>? messages, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public override async Task<ICollection<JObject>?> Work(CancellationToken ct)
    {
        for (index = 0; index < 100; index++)
        {
            await Task.Delay(1000, ct);
        }

        return [JObject.FromObject(new { role = "assistant", content = "i've finished counting to 100" })];
    }
}
