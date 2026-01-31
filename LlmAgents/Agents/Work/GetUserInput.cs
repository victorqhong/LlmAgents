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
        var messageContent = await agent.agentCommunication.WaitForContent(cancellationToken);
        if (messageContent == null)
        {
           return;
        }

        Messages = [LlmApiOpenAi.GetMessage(messageContent)];

        foreach (var message in Messages)
        {
            var content = message.Value<JArray>("content");
            if (content == null) continue;

            foreach (var c in content)
            {
                var type = c.Value<string>("type");
                if (!string.Equals(type, "text"))
                {
                    continue;
                }

                var text = c.Value<string>("text");

                await agent.agentCommunication.SendMessage($"User: {text}", true);
            }
        }
    }
}
