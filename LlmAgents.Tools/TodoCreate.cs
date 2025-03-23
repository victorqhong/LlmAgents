namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Todo;
using System;

public class TodoCreate : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoCreate(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_create",
            description = "Create a todo",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    name = new
                    {
                        type = "string",
                        description = "Name of the todo"
                    },
                    group = new
                    {
                        type = "string",
                        description = "Name of the that contains this todo"
                    },
                    description = new
                    {
                        type = "string",
                        description = "Description of the todo (optional)"
                    }
                },
                required = new[] { "name", "group" }
            }
        }
    });

    public override JObject Function(JObject parameters)
    {
        var result = new JObject();

        var name = parameters["name"]?.ToString();
        if (string.IsNullOrEmpty(name))
        {
            result.Add("error", "name is null or empty");
            return result;
        }

        var group = parameters["group"]?.ToString();
        if (string.IsNullOrEmpty(group))
        {
            result.Add("error", "group is null or empty");
            return result;
        }

        var description = parameters["description"]?.ToString();

        try
        {
            var todoResult = todoDatabase.CreateTodo(name, group, description);
            result.Add("result", todoResult);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
