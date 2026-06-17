namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class StaticMessages : LlmAgentWork
{
    public StaticMessages(ChatCompletionMessageParam staticMessage, LlmAgent agent)
        : this([staticMessage], agent)
    {
    }

    public StaticMessages(ICollection<ChatCompletionMessageParam> staticMessages, LlmAgent agent)
        : base(agent)
    {
        Messages = staticMessages;
    }

    public override Task<ICollection<ChatCompletionMessageParam>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<ChatCompletionMessageParam>?>(null);
    }

    public override Task Run(Session session, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

