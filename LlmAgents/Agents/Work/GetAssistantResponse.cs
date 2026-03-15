namespace LlmAgents.Agents.Work;

using System.Text;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.Extensions.Logging;

public class GetAssistantResponseWork : LlmAgentWork
{
    private readonly ILogger logger;

    public ChatCompletionStreamParser? Parser { get; private set; }

    public string AssistantMessagePrefix { get; set; } = "Assistant: ";

    public List<ChatCompletionFunctionTool>? Tools { get; set; }

    public string ToolChoice { get; set; } = "auto";

    public bool OutputReasoning { get; set; } = false;

    public bool OutputNewLine { get; set; } = true;

    public GetAssistantResponseWork(ILoggerFactory loggerFactory, LlmAgent agent)
        : base(agent)
    {
        logger = loggerFactory.CreateLogger<GetAssistantResponseWork>();
    }

    public override Task<ICollection<ChatCompletionMessageParam>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<ChatCompletionMessageParam>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        Tools ??= agent.GetToolDefinitions();

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
        try
        {
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
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while parsing response");
            return;
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
        if (Parser.Usage != null)
        {
            agent.PostParseUsage?.Invoke(new ChatCompletionUsage { CompletionTokens = Parser.Usage.CompletionTokens, PromptTokens = Parser.Usage.PromptTokens, TotalTokens = Parser.Usage.TotalTokens });
        }
    }
}
