namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

internal class GetAssistantResponseWork : LlmAgentWork<LlmApiOpenAiStreamingCompletionParser>
{
    public GetAssistantResponseWork(LlmAgent agent)
        : base(agent)
    {
    }

    public override ICollection<JObject>? Messages { get; protected set; }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public override async Task OnCompleted(LlmApiOpenAiStreamingCompletionParser? result, CancellationToken ct)
    {
        if (result == null || result.StreamingCompletion == null)
        {
            return;
        }

        await agent.agentCommunication.SendMessage("Assistant: ", true);
        await foreach (var chunk in result.StreamingCompletion)
        {
            await agent.agentCommunication.SendMessage(chunk, false);
        }

        await agent.agentCommunication.SendMessage(string.Empty, true);

        Messages = result.Messages;
    }

    public override async Task<LlmApiOpenAiStreamingCompletionParser?> Work(CancellationToken ct)
    {
        var conversation = agent.RenderConversation();
        var parser = await agent.llmApi.GetStreamingCompletion(conversation, agent.GetToolDefinitions(), "auto", cancellationToken: ct);
        if (parser == null)
        {
            return null;
        }

        return parser;
    }
}
