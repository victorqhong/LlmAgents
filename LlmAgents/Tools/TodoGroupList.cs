namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Todo;
using System;

public class TodoGroupList
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_group_list",
            description = "List all todo groups and associated todos",
            parameters = new
            {
                type = "object",
                properties = new {},
            }
        }
    });

    private readonly TodoDatabase todoDatabase;

    public TodoGroupList(TodoDatabase todoDatabase)
    {
        this.todoDatabase = todoDatabase;

        Tool = new Tool
        {
            Schema = schema,
            Function = Function
        };
    }

    public Tool Tool { get; private set; }

    private JToken Function(JObject parameters)
    {
        var result = new JObject();

        try
        {
            var todoContainers = todoDatabase.ListGroups(true);
            if (todoContainers == null)
            {
                result.Add("error", "could not list groups");
            }
            else
            {
                return JArray.FromObject(todoContainers);
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
