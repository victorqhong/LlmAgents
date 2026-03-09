namespace LlmAgents.Agents.Work;

using System.Text;
using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

public class GetAssistantResponseWork : LlmAgentWork
{
    public LlmApiOpenAiStreamingCompletionParser? Parser { get; private set; }

    public string AssistantMessagePrefix { get; set; } = "Assistant: ";

    public IList<JObject>? Tools { get; set; }

    public string ToolChoice { get; set; } = "auto";

    public bool OutputReasoning { get; set; } = true;

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
        if (Tools == null)
        {
            Tools = agent.GetToolDefinitions();
        }

        var conversation = agent.RenderConversation();
        var parser = await agent.llmApi.GetStreamingCompletion(conversation, Tools, ToolChoice, OutputReasoning, cancellationToken);
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
            if (!string.IsNullOrEmpty(AssistantMessagePrefix))
            {
                await agent.agentCommunication.SendMessage(AssistantMessagePrefix, false);
            }
            
            await foreach (var chunk in Parser.StreamingCompletion)
            {
                await agent.agentCommunication.SendMessage(chunk, false);
            }

            await agent.agentCommunication.SendMessage(string.Empty, true);
        }
        else
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(AssistantMessagePrefix))
            {
                sb.Append(AssistantMessagePrefix);
            }

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
