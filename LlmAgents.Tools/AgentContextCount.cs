using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AgentContextCount : Tool
{
    private readonly ILlmApiMessageProvider messageProvider;

    public AgentContextCount(ToolFactory toolFactory)
        : base(toolFactory)
    {
        messageProvider = toolFactory.Resolve<ILlmApiMessageProvider>();

        ArgumentNullException.ThrowIfNull(messageProvider);
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "agent_context_count",
            description = "Count the number of messages in the conversation context",
            parameters = new
            {
                type = "object",
                properties = new {}
            }
        }
    });

    public override async Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

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