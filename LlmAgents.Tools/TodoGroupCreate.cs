namespace LlmAgents.Tools;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools.Todo;

public class TodoGroupCreate : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoGroupCreate(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "todo_group_create",
            Description = "Create a group for todos",
            Parameters = new()
            {
                Properties = new()
                {
                    { "name", new() { Type = "string", Description = "Name of the group" } },
                    { "description", new() { Type = "string", Description = "Description of the group (optional)" } }
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

        parameters.TryGetValueString("description", string.Empty, out var description);

        try
        {
            var todoResult = todoDatabase.CreateGroup(session, name, description);
            result.Add("result", todoResult);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
