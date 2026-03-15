namespace LlmAgents.Tools;

using LlmAgents.Tools.Todo;
using LlmAgents.State;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using System.Text.Json.Nodes;
using System.Text.Json;
using LlmAgents.Extensions;

public class TodoRead : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "todo_read",
            Description = "Get a todo",
            Parameters = new()
            {
                Properties = new()
                {
                    { "title", new() { Type = "string", Description = "Title of the todo" } },
                    { "group", new() { Type = "string", Description = "Name of the group that contains this todo" } }
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
            result.Add("error", $"{nameof(title)} is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        if (!parameters.TryGetValueString("group", string.Empty, out var group) || string.IsNullOrEmpty(group))
        {
            result.Add("error", "group is null or empty");
            return Task.FromResult<JsonNode>(result);
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
                result.Add("result", JsonSerializer.Serialize(todo));
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}

