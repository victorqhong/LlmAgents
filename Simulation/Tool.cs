namespace Simulation;

using Newtonsoft.Json.Linq;
using System;

public class Tool
{
    public required JObject Schema;
    public required Func<JObject, JObject> Function;

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
}
