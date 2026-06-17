namespace LlmAgents.Agents;

using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public abstract class LlmAgentWork
{
    public abstract Task<ICollection<ChatCompletionMessageParam>?> GetState(CancellationToken ct);

    public ICollection<ChatCompletionMessageParam>? Messages { get; protected set; }

    public readonly LlmAgent agent;

    public LlmAgentWork(LlmAgent agent)
    {
        this.agent = agent;
    }

    public abstract Task Run(Session session, CancellationToken cancellationToken);
}
