namespace LlmAgents.Tools;

using LlmAgents.State;
using LlmAgents.Tools.Todo;
using Newtonsoft.Json.Linq;
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
                    title = new
                    {
                        type = "string",
                        description = "Title of the todo"
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
            result.Add("error", "title is null or empty");
            return Task.FromResult<JToken>(result);
        }

        var group = parameters["group"]?.ToString();
        if (string.IsNullOrEmpty(group))
        {
            result.Add("error", "group is null or empty");
            return Task.FromResult<JToken>(result);
        }

        var description = parameters["description"]?.ToString();

        try
        {
            var todoResult = todoDatabase.CreateTodo(session, title, group, description);
            result.Add("result", todoResult);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}
