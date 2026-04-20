namespace LlmAgents.Tools;

using LlmAgents.Agents;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using System.Text.Json;
using System.Text.Json.Nodes;

public class SessionNew : Tool
{
    private readonly LlmAgent? agent;

    public SessionNew(ToolFactory toolFactory)
        : base(toolFactory)
    {
        agent = toolFactory.ResolveWithDefault<LlmAgent>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "session_new",
            Description = "Starts a new session",
            Parameters = new()
            {
                Properties = new ()
                {
                    { "save", new () { Type = "boolean", Description = "Whether to save the current session" } },
                    { "id", new () { Type = "string", Description = "The new session id" } },
                    { "system_prompt", new () { Type = "string", Description = "The system prompt of the new session (omit to use system prompt from previous session)" } }
                },
                Required = []
            }
        }
    };

    public override async Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();
        if (agent == null)
        {
            result.Add("error", "cannot start new session");
            return result;
        }

        parameters.TryGetValueBool("save", false, out var save);
        parameters.TryGetValueString("id", Guid.NewGuid().ToString(), out var id);
        parameters.TryGetValueString("system_prompt", string.Empty, out var systemPrompt);

        if (agent.SessionCapability.Persistent || save)
        {
            await session.Save();
        }

        var messages = session.GetMessages().ToList();
        if (string.Equals(systemPrompt, string.Empty) && messages[0] is ChatCompletionMessageParamSystem systemMessage && systemMessage.Content is ChatCompletionMessageParamContentString stringContent)
        {
            systemPrompt = stringContent.Content;
        }

        try
        {
            var newSession = new Session(id, session.SessionDatabase);
            if (!string.Equals(systemPrompt, string.Empty))
            {
                newSession.AddMessages([new ChatCompletionMessageParamSystem { Content = new ChatCompletionMessageParamContentString { Content = systemPrompt } }]);
            }

            newSession.AddMessages(messages[^2..]);

            if (agent.SessionCapability.Persistent)
            {
                session.SessionDatabase.CreateSession(newSession);
            }

            var outputMessages = agent.SessionCapability.OutputMessagesOnLoad;
            agent.SessionCapability.OutputMessagesOnLoad = false;
            await agent.SessionCapability.Load(newSession, CancellationToken.None);
            agent.SessionCapability.OutputMessagesOnLoad = outputMessages;

            result.Add("id", id);
            result.Add("result", "success");
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
