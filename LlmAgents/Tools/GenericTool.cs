namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;

public class GenericTool : Tool
{
    private readonly Func<JObject, Task<JToken>> function;

    public GenericTool(JObject schema, Func<JObject, Task<JToken>> function, ToolFactory toolFactory)
        : base(toolFactory)
    {
        Schema = schema;
        this.function = function;
    }

    public override JObject Schema { get; protected set; }

    public override async Task<JToken> Function(JObject parameters)
    {
        return await function(parameters);
    }
}
