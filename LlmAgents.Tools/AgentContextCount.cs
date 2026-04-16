namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.LlmApi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class AgentContextCount : Tool
{
    private readonly ILlmApiMessageProvider messageProvider;

    public AgentContextCount(ToolFactory toolFactory)
        : base(toolFactory)
    {
        messageProvider = toolFactory.Resolve<ILlmApiMessageProvider>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "agent_context_count",
            Description = "Count the number of messages in the conversation context",
            Parameters = new()
            {
                Properties = [],
                Required = []
            }
        }
    };

    public override async Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        try
        {
            result.Add("message_count", await messageProvider.CountMessages());
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
