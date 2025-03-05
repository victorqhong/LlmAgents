namespace Simulation.Tools;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Simulation.Todo;
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

    private JObject Function(JObject parameters)
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
                result.Add("groups", JArray.FromObject(todoContainers));
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
