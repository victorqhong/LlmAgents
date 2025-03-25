using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AdoProjectIterationList : Command
{
    public AdoProjectIterationList(ToolFactory toolFactory)
        : base(toolFactory)
    {
        FileName = "az";
        Arguments = parameters =>
        {
            var project = parameters.Value<string>("project");
            if (string.IsNullOrEmpty(project))
            {
                return null;
            }

            return $"boards iteration project list --project {project}";
        };
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "ado_project_iteration_list",
            description = "Lists the iterations for a project in Azure DevOps",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    project = new
                    {
                        type = "string",
                        description = "Project name"
                    }
                },
                required = new[] { "project" }
            }
        }
    });
}
