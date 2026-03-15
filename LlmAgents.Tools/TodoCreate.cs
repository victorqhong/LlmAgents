namespace LlmAgents.Tools;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools.Todo;

public class TodoCreate : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoCreate(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "todo_create",
            Description = "Create a todo",
            Parameters = new()
            {
                Properties = new()
                {
                    { "title", new() { Type = "string", Description = "Title of the todo" } },
                    { "group", new() { Type = "string", Description = "Name of the that contains this todo" } },
                    { "description", new() { Type = "string", Description = "Description of the todo (optional)" } },
                },
                Required = [ "title", "group" ]
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

        parameters.TryGetValueString("description", string.Empty, out var description);

        try
        {
            var todoResult = todoDatabase.CreateTodo(session, title, group, description);
            result.Add("result", todoResult);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
