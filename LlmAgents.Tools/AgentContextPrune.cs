﻿using LlmAgents.LlmApi;
using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AgentContextPrune : Tool
{
    private readonly LlmApiOpenAi agent;

    public AgentContextPrune(ToolFactory toolFactory)
        : base(toolFactory)
    {
        agent = toolFactory.Resolve<LlmApiOpenAi>();

        ArgumentNullException.ThrowIfNull(agent);
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "agent_context_prune",
            description = "Prune the conversation context",
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

    public override JToken Function(JObject parameters)
    {
        var result = new JObject();

        var messagesKeep = parameters.Value<int?>("messages_keep");
        if (messagesKeep == null)
        {
            result.Add("error", "messages_keep is null or invalid");
            return result;
        }

        result.Add("message_count_before", agent.Messages.Count);

        try
        {
            if (agent.Messages.Count > messagesKeep)
            {
                // keep the first message (system prompt) and last message (a tool call is needed before a tool result)
                var pruneCount = Math.Max(agent.Messages.Count - (int)messagesKeep, 0) - 1;
                agent.Messages.RemoveRange(1, pruneCount - 1);

                // remove messages from the beginning to maintain a well-formatted message list
                while (agent.Messages.Count > 1 && (!string.Equals(agent.Messages[0].Value<string>("role"), "user") && !string.Equals(agent.Messages[0].Value<string>("role"), "system")))
                {
                    agent.Messages.RemoveAt(1);
                }

                result.Add("result", "success");
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        result.Add("message_count_after", agent.Messages.Count);

        return result;
    }
}