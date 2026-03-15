namespace LlmAgents.Tools;

using LlmAgents.Tools.Todo;
using LlmAgents.State;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using System.Text.Json.Nodes;
using System.Text.Json;
using LlmAgents.Extensions;

public class TodoUpdate : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoUpdate(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new() 
    {
        Function = new()
        {
            Name = "todo_update",
            Description = "Update a todo",
            Parameters = new() 
            {
                Properties = new() 
                {
                    { "title", new() { Type = "string", Description = "Title of the todo" } },
                    { "group", new() { Type = "string", Description = "Name of the group that contains this todo" } },
                    { "newTitle", new() { Type = "string", Description = "New title of the todo" } },
                    { "newDescription", new() { Type = "string", Description = "New description of the todo" } },
                    { "newGroup", new() { Type = "string", Description = "New group of the todo" } },
                    { "newDueDate", new() { Type = "string", Description = "New due date of the todo" } },
                    { "newCompleted", new() { Type = "boolean", Description = "New completed state of the todo" } },
                },
                Required = ["title", "group"]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("title", string.Empty, out var title) || string.IsNullOrEmpty(title))
        {
            result.Add("error", "title is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        if (!parameters.TryGetValueString("group", string.Empty, out var group) || string.IsNullOrEmpty(group))
        {
            result.Add("error", "group is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        try
        {
            parameters.TryGetValueString("newTitle", string.Empty, out var newTitle);
            parameters.TryGetValueString("newDescription", string.Empty, out var newDescription);
            parameters.TryGetValueString("newGroup", string.Empty, out var newGroup);
            parameters.TryGetValueString("newDueDate", string.Empty, out var newDueDate);
            var parsedNewCompleted = parameters.TryGetValueBool("newCompleted", false, out var newCompleted);

            var success = todoDatabase.UpdateTodo(session, title, group, newTitle, newGroup, newDescription, newDueDate, parsedNewCompleted ? newCompleted : null);
            result.Add("success", success);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}

