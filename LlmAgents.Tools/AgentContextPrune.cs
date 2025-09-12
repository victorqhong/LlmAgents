using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AgentContextPrune : Tool
{
    private readonly ILlmApiMessageProvider messageProvider;

    public AgentContextPrune(ToolFactory toolFactory)
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
            name = "agent_context_prune",
            description = "Prune the conversation context. Not all messages can be pruned in order to keep a consistent context.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    messages_keep = new
                    {
                        type = "number",
                        description = "The number of messages to keep"
                    }
                },
                required = new[] { "messages_keep" }
            }
        }
    });

    public override async Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var messagesKeep = parameters.Value<int?>("messages_keep");
        if (messagesKeep == null)
        {
            result.Add("error", "messages_keep is null or invalid");
            return result;
        }

        try
        {
            await messageProvider.PruneContext(messagesKeep.Value);
            result.Add("result", "success");
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}