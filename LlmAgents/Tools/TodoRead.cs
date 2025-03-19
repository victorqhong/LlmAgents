namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Todo;
using System;

public class TodoRead
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_read",
            description = "Get a todo",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    title = new
                    {
                        type = "string",
                        description = "Title of the todo"
                    },
                    group = new
                    {
                        type = "string",
                        description = "Name of the group that contains this todo"
                    }
                },
                required = new[] { "title", "group" }
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

        var group = parameters["group"]?.ToString();
        if (string.IsNullOrEmpty(group))
        {
            result.Add("error", $"{nameof(group)} is null or empty");
            return result;
        }

        try
        {
            var todo = todoDatabase.GetTodo(title, group);
            if (todo == null)
            {
                result.Add("error", $"could not find todo with tile '{title}' in group '{group}'");
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

