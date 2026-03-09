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

    public bool OutputReasoning { get; set; } = false;

    public bool OutputNewLine { get; set; } = true;

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

        agent.PreGetResponse?.Invoke();
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

        var sb = new StringBuilder();

        var sentPrefix = false;
        var sentChunk = false;
        await foreach (var chunk in Parser.StreamingCompletion)
        {
            if (!sentPrefix)
            {
                if (!string.IsNullOrEmpty(AssistantMessagePrefix))
                {
                    if (agent.StreamOutput)
                    {
                        await agent.agentCommunication.SendMessage(AssistantMessagePrefix, false);
                    }
                    else
                    {
                        sb.Append(AssistantMessagePrefix);
                    }
                }

                sentPrefix = true;
            }

            if (agent.StreamOutput)
            {
                await agent.agentCommunication.SendMessage(chunk, false);
            }
            else
            {
                sb.Append(chunk);
            }

            sentChunk = true;
        }

        if (sentChunk && OutputNewLine)
        {
            if (agent.StreamOutput)
            {
                await agent.agentCommunication.SendMessage(string.Empty, true);
            }
            else
            {
                sb.Append('\n');
            }
        }

        if (!agent.StreamOutput)
        {
            await agent.agentCommunication.SendMessage(sb.ToString(), false);
        }

        Messages = Parser.Messages;

        agent.PostSendMessage?.Invoke();
        agent.PostParseUsage?.Invoke(new TokenUsage { CompletionTokens = Parser.UsageCompletionTokens, PromptTokens = Parser.UsagePromptTokens, TotalTokens = Parser.UsageTotalTokens });
    }
}
