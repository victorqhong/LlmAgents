namespace LlmAgents.LlmApi.Tools;

using LlmAgents.Tools;
using Newtonsoft.Json.Linq;

public abstract class ToolProvider
{
    public readonly List<Tool> Tools = [];
    public readonly List<JObject> ToolDefinitions = [];
    public readonly Dictionary<string, Tool> ToolMap = [];

    public void AddTool(params Tool[] tools)
    {
        foreach (var tool in tools)
        {
            AddTool(tool);
        }
    }

    public void AddTool(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        Tools.Add(tool);
        ToolDefinitions.Add(tool.Schema);
        ToolMap.Add(tool.Name, tool);
    }
}
