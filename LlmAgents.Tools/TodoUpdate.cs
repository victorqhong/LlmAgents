namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Tools.Todo;
using System;

public class TodoUpdate : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoUpdate(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_update",
            description = "Update a todo",
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
                    },
                    newTitle = new
                    {
                        type = "string",
                        description = "New title of the todo"
                    },
                    newDescription = new
                    {
                        type = "string",
                        description = "New description of the todo"
                    },
                    newGroup = new
                    {
                        type = "string",
                        description = "New group of the todo"
                    },
                    newDueDate = new
                    {
                        type = "string",
                        description = "New due date of the todo"
                    },
                    newCompleted = new
                    {
                        type = "boolean",
                        description = "New completed state of the todo"
                    }
                },
                required = new[] { "title", "group" }
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
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
            string? newTitle = parameters.Value<string>("newTitle");
            string? newDescription = parameters.Value<string>("newDescription");
            string? newGroup = parameters.Value<string>("newGroup");
            string? newDueDate = parameters.Value<string>("newDueDate");
            bool? newCompleted = parameters.Value<bool>("newCompleted");

            var success = todoDatabase.UpdateTodo(toolFactory.Session, title, group, newTitle, newGroup, newDescription, newDueDate, newCompleted);
            result.Add("success", success);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}

