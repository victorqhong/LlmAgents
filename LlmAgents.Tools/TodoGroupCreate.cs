namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using LlmAgents.Todo;
using System;

public class TodoGroupCreate : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoGroupCreate(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_group_create",
            description = "Create a group for todos",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    name = new
                    {
                        type = "string",
                        description = "Name of the group"
                    },
                    description = new
                    {
                        type = "string",
                        description = "Description of the group (optional)"
                    }
                },
                required = new[] { "name" }
            }
        }
    });

    public override async Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var name = parameters["name"]?.ToString();
        if (string.IsNullOrEmpty(name))
        {
            result.Add("error", "name is null or empty");
            return result;
        }

        var description = parameters["description"]?.ToString();

        try
        {
            var todoResult = todoDatabase.CreateGroup(name, description);
            result.Add("result", todoResult);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
