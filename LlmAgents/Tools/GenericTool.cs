namespace LlmAgents.Tools;

using LlmAgents.State;
using Newtonsoft.Json.Linq;
using System;

public class GenericTool : Tool
{
    private readonly Func<Session, JObject, Task<JToken>> function;

    public GenericTool(JObject schema, Func<Session, JObject, Task<JToken>> function, ToolFactory toolFactory)
        : base(toolFactory)
    {
        Schema = schema;
        this.function = function;
    }

    public override JObject Schema { get; protected set; }

    public override async Task<JToken> Function(Session session, JObject parameters)
    {
        return await function(session, parameters);
    }
}
