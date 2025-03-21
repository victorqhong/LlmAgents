namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;

public class GenericTool : Tool
{
    private readonly Func<JObject, JToken> function;

    public GenericTool(JObject schema, Func<JObject, JToken> function)
        : base(null)
    {
        Schema = schema;
        this.function = function;
    }

    public override JObject Schema { get; protected set; }

    public override JToken Function(JObject parameters)
    {
        return function(parameters);
    }
}
