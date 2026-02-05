namespace LlmAgents.Agents.Work;

using System.Text;
using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

public class GetAssistantResponseWork : LlmAgentWork
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

        if (Parser.StreamingCompletion == null)
        {
            return;
        }

        if (agent.StreamOutput)
        {
            await agent.agentCommunication.SendMessage("Assistant: ", true);
            await foreach (var chunk in Parser.StreamingCompletion)
            {
                await agent.agentCommunication.SendMessage(chunk, false);
            }

            await agent.agentCommunication.SendMessage(string.Empty, true);
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append("Assistant: ");
            await foreach (var chunk in Parser.StreamingCompletion)
            {
                sb.Append(chunk);
            }
            sb.Append('\n');

            await agent.agentCommunication.SendMessage(sb.ToString(), true);
        }

        Messages = Parser.Messages;

        agent.PostSendMessage?.Invoke();
        agent.PostParseUsage?.Invoke(new TokenUsage { CompletionTokens = Parser.UsageCompletionTokens, PromptTokens = Parser.UsagePromptTokens, TotalTokens = Parser.UsageTotalTokens });
    }
}
