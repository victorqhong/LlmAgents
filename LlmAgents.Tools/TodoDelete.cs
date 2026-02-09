namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Tools.Todo;
using System;
using LlmAgents.State;
public class TodoDelete : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoDelete(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_delete",
            description = "Delete a todo",
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

        try
        {
            var todoResult = todoDatabase.DeleteTodo(session, title, group);
            result.Add("result", todoResult);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}
