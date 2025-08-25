namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Tools.Todo;
using System;

public class TodoGroupList : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoGroupList(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_group_list",
            description = "List all todo groups and associated todos",
            parameters = new
            {
                type = "object",
                properties = new { },
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        try
        {
            var todoContainers = todoDatabase.ListGroups(toolFactory.Session, true);
            if (todoContainers == null)
            {
                result.Add("error", "could not list groups");
            }
            else
            {
                return Task.FromResult<JToken>(JArray.FromObject(todoContainers));
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}
