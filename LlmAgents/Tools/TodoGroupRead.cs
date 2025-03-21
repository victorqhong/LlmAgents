namespace LlmAgents.Tools;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LlmAgents.Todo;
using System;

public class TodoGroupRead : Tool
{
    private readonly TodoDatabase todoDatabase;

    public TodoGroupRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
        todoDatabase = toolFactory.Resolve<TodoDatabase>();
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "todo_group_read",
            description = "Get a todo group",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    name = new
                    {
                        type = "string",
                        description = "Name of the group"
                    }
                },
                required = new[] { "name" }
            }
        }
    });

    public override JObject Function(JObject parameters)
    {
        var result = new JObject();

        var name = parameters["name"]?.ToString();
        if (string.IsNullOrEmpty(name))
        {
            result.Add("error", "name is null or empty");
            return result;
        }

        try
        {
            var todoContainer = todoDatabase.GetGroup(name);
            if (todoContainer == null)
            {
                result.Add("error", "could not find group");
            }
            else
            {
                result.Add("result", JsonConvert.SerializeObject(todoContainer));
            }
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
