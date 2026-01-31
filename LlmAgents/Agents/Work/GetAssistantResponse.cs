namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

internal class GetAssistantResponseWork : LlmAgentWork
{
    public LlmApiOpenAiStreamingCompletionParser? Parser { get; private set; }

    public GetAssistantResponseWork(LlmAgent agent)
        : base(agent)
    {
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        var conversation = agent.RenderConversation();
        var parser = await agent.llmApi.GetStreamingCompletion(conversation, agent.GetToolDefinitions(), "auto", cancellationToken: cancellationToken);
        if (parser == null)
        {
            return;
        }

        Parser = parser;

        if (parser.StreamingCompletion == null)
        {
            return;
        }

        await agent.agentCommunication.SendMessage("Assistant: ", true);
        await foreach (var chunk in parser.StreamingCompletion)
        {
            await agent.agentCommunication.SendMessage(chunk, false);
        }

        await agent.agentCommunication.SendMessage(string.Empty, true);

        Messages = parser.Messages;
    }
}
