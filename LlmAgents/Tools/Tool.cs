namespace LlmAgents.Tools;

using LlmAgents.State;
using Newtonsoft.Json.Linq;
using System;

public abstract class Tool
{
    protected readonly ToolFactory toolFactory;

    public Tool(ToolFactory toolFactory)
    {
        this.toolFactory = toolFactory;
    }

    public abstract JObject Schema { get; protected set; }

    public string Name
    {
        get
        {
            var name = Schema["function"]?["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
            {
                throw new NullReferenceException();
            }

            return name;
        }
    }

    public abstract Task<JToken> Function(JObject parameters);

    public virtual void Save(string sessionId, StateDatabase stateDatabase) { }

    public virtual void Load(string sessionId, StateDatabase stateDatabase) { }
}
