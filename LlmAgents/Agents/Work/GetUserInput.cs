namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

public class GetUserInputWork : LlmAgentWork<ICollection<JObject>>
{
    public GetUserInputWork(LlmAgent agent)
        : base(agent)
    {
    }

    public override ICollection<JObject>? Messages { get; protected set; }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public override async Task OnCompleted(ICollection<JObject>? messages, CancellationToken ct)
    {
        if (messages == null)
        {
            return;
        }

        Messages = messages;

        foreach (var message in messages)
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

    public override async Task<ICollection<JObject>?> Work(CancellationToken ct)
    {
       var messageContent = await agent.agentCommunication.WaitForContent(ct); 
       if (messageContent == null)
       {
           return null;
       }

       return [LlmApiOpenAi.GetMessage(messageContent)];
    }
}
