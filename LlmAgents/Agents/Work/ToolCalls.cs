namespace LlmAgents.Agents.Work;

using LlmAgents.LlmApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class ToolCalls : LlmAgentWork
{
    public LlmApiOpenAiStreamingCompletionParser? Parser { get; private set; }

    public ToolCalls(LlmAgent agent)
        : base(agent)
    {
    }

    public override Task<ICollection<JObject>?> GetState(CancellationToken ct)
    {
        return Task.FromResult<ICollection<JObject>?>(null);
    }

    public async override Task Run(CancellationToken cancellationToken)
    {
        var conversation = agent.RenderConversation();
        Messages = await ProcessToolCalls(conversation);
    }

    private async Task<ICollection<JObject>?> ProcessToolCalls(List<JObject> messages)
    {
        var toolCalls = messages[^1].Value<JArray>("tool_calls");
        if (toolCalls == null)
        {
            return null;
        }

        var toolMessages = new List<JObject>(); 
        foreach (JObject toolCall in toolCalls.Cast<JObject>())
        {
            var id = toolCall.Value<string>("id");

            var function = toolCall.Value<JObject>("function");
            if (function == null)
            {
                toolMessages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    content = $"Invalid tool call: tool call does not contain 'function' property"
                }));

                continue;
            }

            var name = function.Value<string>("name");
            if (string.IsNullOrEmpty(name))
            {
                toolMessages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = $"Invalid tool call: tool call does not contain 'name' property"
                }));

                continue;
            }

            var arguments = function.Value<string>("arguments");
            if (arguments == null)
            {
                toolMessages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = $"Invalid tool call: tool call does not contain 'arguments' property"
                }));

                continue;
            }

            string toolContent;
            try
            {
                await agent.agentCommunication.SendMessage($"Calling tool '{name}' with arguments '{arguments}'", true);
                var toolResult = await agent.CallTool(name, JObject.Parse(arguments));
                if (toolResult == null)
                {
                    toolMessages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        name,
                        content = $"Invalid tool call: tool {name} could not be found"
                    }));

                    continue;
                }

                toolContent = JsonConvert.SerializeObject(toolResult);
            }
            catch (Exception ex)
            {
                toolContent = $"Got exception: {ex.Message}";
            }

            toolMessages.Add(JObject.FromObject(new
            {
                role = "tool",
                tool_call_id = id,
                name,
                content = toolContent
            }));
        }

        return toolMessages;
    }
}
