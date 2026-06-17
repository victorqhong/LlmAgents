namespace LlmAgents.Agents.Work;

using System.Text;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
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

    public bool OutputResponse { get; set; } = true;

    public bool StreamOutput { get; set; }

    public Action<Session>? PreGetResponse { get; set; }

    public Action<Session>? PostSendMessage { get; set; }

    public Action<Session, ChatCompletionUsage>? PostParseUsage { get; set; }

    public GetAssistantResponseWork(ILoggerFactory loggerFactory, LlmAgent agent)
        : base(agent)
    {
        logger = loggerFactory.CreateLogger<GetAssistantResponseWork>();
    }

    public override Task<ICollection<ChatCompletionMessageParam>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<ChatCompletionMessageParam>?>(null);
    }

    public async override Task Run(Session session, CancellationToken cancellationToken)
    {
        Tools ??= agent.ToolCallCapability.GetToolDefinitions(session);

        var conversation = session.GetMessages().ToList();

        PreGetResponse?.Invoke(session);

        var completionResult = await agent.llmApi.GetStreamingCompletion(conversation, Tools, ToolChoice, cancellationToken);
        if (completionResult.Error == LlmApiOpenAi.CompletionHttpResult.CompletionError.Throttled)
        {
            PruneContext(conversation);
            completionResult = await agent.llmApi.GetStreamingCompletion(conversation, Tools, ToolChoice, cancellationToken);
        }

        if (completionResult.CompletionStream == null)
        {
            logger.LogError("CompletionStream is null");
            return;
        }

        Parser = new ChatCompletionStreamParser(completionResult.CompletionStream) { OutputReasoning = OutputReasoning };
        Parser.Parse(cancellationToken);

        if (Parser.StreamingCompletion == null)
        {
            logger.LogError("StreamingCompletion is null");
            return;
        }

        var sessionCommunication = agent.SessionCapability.GetSessionCommunication(session);

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
                        if (StreamOutput && OutputResponse)
                        {
                            await sessionCommunication.SendMessage(AssistantMessagePrefix, false);
                        }
                        else
                        {
                            sb.Append(AssistantMessagePrefix);
                        }
                    }

                    sentPrefix = true;
                }

                if (StreamOutput && OutputResponse)
                {
                    await sessionCommunication.SendMessage(chunk, false);
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
            if (StreamOutput && OutputResponse)
            {
                await sessionCommunication.SendMessage(string.Empty, true);
            }
            else
            {
                sb.Append('\n');
            }
        }

        if (sentChunk && !StreamOutput && OutputResponse)
        {
            await sessionCommunication.SendMessage(sb.ToString(), false);
        }

        Messages = Parser.Messages;

        PostSendMessage?.Invoke(session);
        if (Parser.Usage != null)
        {
            PostParseUsage?.Invoke(session, new ChatCompletionUsage { CompletionTokens = Parser.Usage.CompletionTokens, PromptTokens = Parser.Usage.PromptTokens, TotalTokens = Parser.Usage.TotalTokens });
        }
    }

    private void PruneContext(List<ChatCompletionMessageParam> conversation)
    {
        logger.LogInformation("Pruning context");
        foreach (var message in conversation)
        {
            if (message is not ChatCompletionMessageParamTool toolMessage)
            {
                continue;
            }

            if (string.Equals(toolMessage.Name, "file_read"))
            {
                toolMessage.Content = new ChatCompletionMessageParamContentString { Content = "<tool content has been pruned>" };
            }
        }
        logger.LogInformation("Finished pruning context");
    }
}
