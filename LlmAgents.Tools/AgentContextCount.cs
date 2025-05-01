using LlmAgents.Agents;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AgentContextCount : Tool
{
    private readonly LlmAgentApi agent;

    public AgentContextCount(ToolFactory toolFactory)
        : base(toolFactory)
    {
        agent = toolFactory.Resolve<LlmAgentApi>();

        ArgumentNullException.ThrowIfNull(agent);
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

    public override JToken Function(JObject parameters)
    {
        var result = new JObject();

        try
        {
            result.Add("message_count", agent.Messages.Count);

        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}