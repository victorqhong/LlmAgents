namespace LlmAgents.Tools;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tools.Todo;

public class TodoGroupList : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoGroupList(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "todo_group_list",
            Description = "List all todo groups and associated todos",
            Parameters = new()
            {
                Type = "object",
                Properties = [],
                Required = []
            },
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        try
        {
            var todoContainers = todoDatabase.ListGroups(session, true);
            if (todoContainers == null)
            {
                result.Add("error", "could not list groups");
            }
            else
            {
                result.Add("result", JsonSerializer.Serialize(todoContainers));
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
