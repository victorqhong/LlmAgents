namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public abstract class Tool
{
    protected readonly ToolFactory toolFactory;

    public Tool(ToolFactory toolFactory)
    {
        this.toolFactory = toolFactory;
    }

    public abstract ChatCompletionFunctionTool Schema { get; protected set; }

    public string Name
    {
        get
        {
            return Schema.Function.Name;
        }
    }

    public abstract Task<JsonNode> Function(Session session, JsonDocument parameters);

    public virtual void Save(Session session, StateDatabase stateDatabase) { }

    public virtual void Load(Session session, StateDatabase stateDatabase) { }
}
