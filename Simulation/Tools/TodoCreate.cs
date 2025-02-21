namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;

public class TodoCreate
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_create",
            description = "Create a todo list item",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    name = new
                    {
                        type = "string",
                        description = "Name of the todo item"
                    },
                    container = new
                    {
                        type = "string",
                        description = "Name of the list that contains this todo item"
                    },
                    description = new
                    {
                        type = "string",
                        description = "Description of the todo item (optional)"
                    }
                },
                required = new[] { "name", "container" }
            }
        }
    });

    public TodoCreate(string todoDatabase)
    {
        TodoDatabase = todoDatabase;

        Tool = new Tool
        {
            Schema = schema,
            Function = Function
        };
    }

    public Tool Tool { get; private set; }

    public string TodoDatabase { get; private set; }

    private JObject Function(JObject parameters)
    {
        var result = new JObject();

        var name = parameters["name"]?.ToString();
        if (string.IsNullOrEmpty(name))
        {
            result.Add("error", "name is null or empty");
            return result;
        }

        var container = parameters["container"]?.ToString();
        if (string.IsNullOrEmpty(container))
        {
            result.Add("error", "container is null or empty");
            return result;
        }

        var description = parameters["description"]?.ToString();

        try
        {
            CreateTodo(name, container, description);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }

    private void CreateTodo(string name, string container, string? description)
    {

    }
}
