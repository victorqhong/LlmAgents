namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Tools.Todo;
using System;
using LlmAgents.State;

public class TodoRead : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
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

    public override Task<JToken> Function(Session session, JObject parameters)
    {
        var result = new JObject();

        var title = parameters["title"]?.ToString();
        if (string.IsNullOrEmpty(title))
        {
            result.Add("error", $"{nameof(title)} is null or empty");
            return Task.FromResult<JToken>(result);
        }

        var group = parameters["group"]?.ToString();
        if (string.IsNullOrEmpty(group))
        {
            result.Add("error", $"{nameof(group)} is null or empty");
            return Task.FromResult<JToken>(result);
        }

        try
        {
            var todo = todoDatabase.GetTodo(session, title, group);
            if (todo == null)
            {
                result.Add("error", $"could not find todo with title '{title}' in group '{group}'");
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

        return Task.FromResult<JToken>(result);
    }
}

