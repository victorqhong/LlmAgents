namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

public class GetUserInputWork : LlmAgentWork
{
    public GetUserInputWork(LlmAgent agent)
        : base(agent)
    {
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        agent.PreWaitForContent?.Invoke();
        var messageContent = await agent.agentCommunication.WaitForContent(cancellationToken);
        if (messageContent == null)
        {
           return;
        }

        agent.PostReceiveContent?.Invoke();

        Messages = [LlmApiOpenAi.GetMessage(messageContent)];
    }
}
