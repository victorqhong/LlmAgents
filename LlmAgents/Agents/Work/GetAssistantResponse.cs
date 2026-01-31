namespace LlmAgents.Agents.Work;

using System.Text;
using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

internal class GetAssistantResponseWork : LlmAgentWork
{
    public readonly bool StreamOutput;

    public LlmApiOpenAiStreamingCompletionParser? Parser { get; private set; }

    public GetAssistantResponseWork(bool streamOutput, LlmAgent agent)
        : base(agent)
    {
        StreamOutput = streamOutput;
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

        if (StreamOutput)
        {
            await agent.agentCommunication.SendMessage("Assistant: ", true);
            await foreach (var chunk in parser.StreamingCompletion)
            {
                await agent.agentCommunication.SendMessage(chunk, false);
            }

            await agent.agentCommunication.SendMessage(string.Empty, true);
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append("Assistant: ");
            await foreach (var chunk in parser.StreamingCompletion)
            {
                sb.Append(chunk);
            }
            sb.Append('\n');

            await agent.agentCommunication.SendMessage(sb.ToString(), true);
        }

        Messages = parser.Messages;
    }
}
