namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using Simulation.Todo;
using System;

public class TodoRead
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_read",
            description = "Get a todo list item",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    title = new
                    {
                        type = "string",
                        description = "Title of the todo item"
                    },
                    container = new
                    {
                        type = "string",
                        description = "Name of the list that contains this todo item"
                    }
                },
                required = new[] { "title", "container" }
            }
        }
    });

    private readonly TodoDatabase todoDatabase;

    public TodoRead(TodoDatabase todoDatabase)
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

        var title = parameters["title"]?.ToString();
        if (string.IsNullOrEmpty(title))
        {
            result.Add("error", $"{nameof(title)} is null or empty");
            return result;
        }

        var container = parameters["container"]?.ToString();
        if (string.IsNullOrEmpty(container))
        {
            result.Add("error", $"{nameof(container)} is null or empty");
            return result;
        }

        try
        {
            var todo = todoDatabase.GetTodo(title, container);
            if (todo == null)
            {
                result.Add("error", $"could not find todo with tile '{title}' in container '{container}'");
            }
            else
            {
                result.Add("result", Newtonsoft.Json.JsonConvert.SerializeObject(todo));
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}

