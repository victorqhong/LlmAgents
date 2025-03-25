using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AdoWorkItemCreate : Command
{
    public AdoWorkItemCreate(ToolFactory toolFactory)
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

            var title = parameters.Value<string>("title");
            if (string.IsNullOrEmpty(title))
            {
                return null;
            }

            var type = parameters.Value<string>("type");
            if (string.IsNullOrEmpty(type))
            {
                return null;
            }

            var sb = new System.Text.StringBuilder();

            var area = parameters.Value<string>("area");
            if (!string.IsNullOrEmpty(area))
            {
                sb.Append($"--area {area}");
            }

            var description = parameters.Value<string>("description");
            if (!string.IsNullOrEmpty(description))
            {
                sb.Append($"--description \"{description}\"");
            }

            var iteration = parameters.Value<string>("iteration");
            if (!string.IsNullOrEmpty(iteration))
            {
                sb.Append($"--iteration \"{iteration}\"");
            }

            return $"boards work-item create --title \"{title}\" --type \"{type}\" --project {project} {sb}";
        };
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "ado_workitem_create",
            description = "Create a work item for a project in Azure DevOps",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    title = new
                    {
                        type = "string",
                        description = "Name of the work item"
                    },
                    type = new
                    {
                        type = "string",
                        @enum = new[] { "Bug", "Epic", "Feature", "Issue", "Task", "Test Case", "User Story" },
                        description = "Type of the work item"
                    },
                    area = new
                    {
                        type = "string",
                        description = "Area path of the work item"
                    },
                    description = new
                    {
                        type = "string",
                        description = "Description of the work item"
                    },
                    iteration = new
                    {
                        type = "string",
                        description = "Iteration of the work item",
                    },
                    project = new
                    {
                        type = "string",
                        description = "Project name"
                    }
                },
                required = new[] { "project", "title", "type" }
            }
        }
    });
}
