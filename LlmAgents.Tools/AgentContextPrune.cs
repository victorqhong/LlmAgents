namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class AgentContextPrune : Tool
{
    private readonly ILlmApiMessageProvider messageProvider;

    public AgentContextPrune(ToolFactory toolFactory)
        : base(toolFactory)
    {
        messageProvider = toolFactory.Resolve<ILlmApiMessageProvider>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "agent_context_prune",
            Description = "Prune the conversation context",
            Parameters = new()
            {
                Properties = new()
                {
                    { "messages_keep", new() { Type = "number", Description = "The number of messages to keep" } }
                },
                Required = ["messages_keep"]
            }
        }
    };

    public override async Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueInt("messages_keep", out var messagesKeep))
        {
            result.Add("error", "messages_keep is null or invalid");
            return result;
        }

        if (messagesKeep == null)
        {
            result.Add("error", "could not get value for messages_keep");
            return result;
        }

        result.Add("message_count_before", await messageProvider.CountMessages());

        try
        {
            await messageProvider.PruneContext(messagesKeep.Value);
            result.Add("result", "success");
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        result.Add("message_count_after", await messageProvider.CountMessages());

        return result;
    }
}
