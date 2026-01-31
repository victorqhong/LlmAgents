using Newtonsoft.Json.Linq;

namespace LlmAgents.Agents.Work;

public class CountingWork : LlmAgentWork
{
    private volatile int index;

    public CountingWork(LlmAgent agent)
        : base(agent)
    {
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>([JObject.FromObject(new { role = "assistant", content = $"i've counted to {index}" })]);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        for (index = 0; index < 100; index++)
        {
            await Task.Delay(1000, cancellationToken);
        }

        ICollection<JObject>? messages = [JObject.FromObject(new { role = "assistant", content = "i've finished counting to 100" })];
        Messages = messages;
    }
}
