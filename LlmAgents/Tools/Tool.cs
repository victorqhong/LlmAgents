namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;

public abstract class Tool
{
    public Tool(ToolFactory toolFactory)
    {
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

    public abstract JToken Function(JObject parameters);
}
