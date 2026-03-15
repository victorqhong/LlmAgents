namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class StaticMessages : LlmAgentWork
{
    public StaticMessages(ICollection<ChatCompletionMessageParam> staticMessages, LlmAgent agent)
        : base(agent)
    {
        Messages = staticMessages;
    }

    public override Task<ICollection<ChatCompletionMessageParam>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<ChatCompletionMessageParam>?>(null);
    }

    public override Task Run(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

