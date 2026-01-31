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
        await ProcessToolCalls(conversation);

        var parser = await agent.llmApi.GetStreamingCompletion(conversation, agent.GetToolDefinitions(), "auto", cancellationToken: cancellationToken);
        if (parser == null)
        {
            return;
        }

        if (parser.StreamingCompletion == null)
        {
            return;
        }

        await agent.agentCommunication.SendMessage("Assistant: ", true);
        await foreach (var chunk in parser.StreamingCompletion)
        {
            await agent.agentCommunication.SendMessage(chunk, false);
        }

        await agent.agentCommunication.SendMessage(string.Empty, true);

        Messages = parser.Messages;
    }

    private async Task ProcessToolCalls(List<JObject> messages)
    {
        var toolCalls = messages[^1].Value<JArray>("tool_calls");
        if (toolCalls == null)
        {
            return;
        }

        foreach (JObject toolCall in toolCalls.Cast<JObject>())
        {
            var id = toolCall.Value<string>("id");

            var function = toolCall.Value<JObject>("function");
            if (function == null)
            {
                messages.Add(JObject.FromObject(new
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
                messages.Add(JObject.FromObject(new
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
                messages.Add(JObject.FromObject(new
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
                // await agentCommunication.SendMessage($"Calling tool '{name}' with arguments '{arguments}'", true);
                var toolResult = await agent.CallTool(name, JObject.Parse(arguments));
                if (toolResult == null)
                {
                    messages.Add(JObject.FromObject(new
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

            messages.Add(JObject.FromObject(new
            {
                role = "tool",
                tool_call_id = id,
                name,
                content = toolContent
            }));
        }
    }
}
