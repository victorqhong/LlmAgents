namespace LlmAgents.Agents.Work;

using System.Text.Json;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.Extensions.Logging;

public class ToolCalls : LlmAgentWork
{
    private readonly ILogger logger;

    public ToolCalls(ILoggerFactory loggerFactory, LlmAgent agent)
        : base(agent)
    {
        logger = loggerFactory.CreateLogger(nameof(ToolCalls));
    }

    public override Task<ICollection<ChatCompletionMessageParam>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<ChatCompletionMessageParam>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        var conversation = agent.RenderConversation();
        if (conversation.Count < 1)
        {
            return;
        }

        if (conversation[^1] is not ChatCompletionMessageParamAssistant assistantMessage)
        {
            return;
        }

        Messages = await ProcessToolCalls(assistantMessage, cancellationToken);
    }

    private async Task<ICollection<ChatCompletionMessageParam>?> ProcessToolCalls(ChatCompletionMessageParamAssistant assistantMessage, CancellationToken cancellationToken)
    {
        if (assistantMessage.ToolCalls == null)
        {
            return [];
        }

        var toolMessages = new List<ChatCompletionMessageParam>(); 
        foreach (var toolCall in assistantMessage.ToolCalls)
        {
            string toolContent;
            try
            {
                logger.LogInformation("Calling tool '{name}' with arguments '{arguments}'", toolCall.Function.Name, toolCall.Function.Arguments);

                var toolResult = await agent.CallTool(toolCall.Function.Name, JsonDocument.Parse(toolCall.Function.Arguments));
                if (toolResult == null)
                {
                    toolMessages.Add(new ChatCompletionMessageParamTool
                    {
                        ToolCallId = toolCall.Id,
                        Content = new ChatCompletionMessageParamContentString { Content = $"Invalid tool call: tool {toolCall.Function.Name} could not be found" },
                    });

                    continue;
                }

                toolContent = toolResult.ToJsonString();
            }
            catch (Exception ex)
            {
                toolContent = $"Got exception: {ex.Message}";
            }

            toolMessages.Add(new ChatCompletionMessageParamTool
            {
                ToolCallId = toolCall.Id,
                Name = toolCall.Function.Name,
                Content = new ChatCompletionMessageParamContentString { Content = toolContent },
            });
        }

        return toolMessages;
    }
}
