namespace LlmAgents.Tools;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools.Todo;

public class TodoGroupRead : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoGroupRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "todo_group_read",
            Description = "Get a todo group",
            Parameters = new()
            {
                Properties = new()
                {
                    { "name", new() { Type = "string", Description = "Name of the group" } }
                },
                Required = ["name"]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("name", string.Empty, out var name) || string.IsNullOrEmpty(name))
        {
            result.Add("error", "name is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        try
        {
            var todoContainer = todoDatabase.GetGroup(session, name);
            if (todoContainer == null)
            {
                result.Add("error", "could not find group");
            }
            else
            {
                result.Add("result", JsonSerializer.Serialize(todoContainer));
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
