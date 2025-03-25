using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AdoProjectAreaList : Command
{
    public AdoProjectAreaList(ToolFactory toolFactory)
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

            return $"boards area project list --project {project}";
        };
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "ado_project_area_list",
            description = "Lists the area paths for a project in Azure DevOps",
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
