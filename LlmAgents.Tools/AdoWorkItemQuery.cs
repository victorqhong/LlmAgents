using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class AdoWorkItemQuery : Command
{
    public AdoWorkItemQuery(ToolFactory toolFactory)
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

            var sb = new System.Text.StringBuilder();

            var type = parameters.Value<string>("type");
            if (!string.IsNullOrEmpty(type) && !type.Equals("Any"))
            {
                sb.Append($"AND [System.WorkItemType] = '{type}'");
            }

            var state = parameters.Value<string>("state");
            if (!string.IsNullOrEmpty(state) && !state.Equals("Any"))
            {
                sb.Append($" AND [System.State] = '{state}'");
            }

            var wiql = $"SELECT [System.Id], [System.State], [System.Title], [System.Description] FROM workitems WHERE [System.TeamProject] = '{project}' {sb} ORDER BY [System.ChangedDate] DESC";

            return $"boards query --project {project} --wiql \"{wiql}\"";
        };
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "ado_workitem_query",
            description = "Query work items for a project in Azure DevOps",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    project = new
                    {
                        type = "string",
                        description = "Project name"
                    },
                    type = new
                    {
                        type = "string",
                        @enum = new[] { "Any", "Bug", "Epic", "Feature", "Issue", "Task", "Test Case", "User Story" },
                        description = "Type of work items to query"
                    },
                    state = new
                    {
                        type = "string",
                        @enum = new[] { "Any", "New", "Active", "Resolved", "Closed", "Removed" },
                        description = "State of work items to query"
                    }
                },
                required = new[] { "project" }
            }
        }
    });
}
