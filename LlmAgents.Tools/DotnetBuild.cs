using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class DotnetBuild : Command
{
    public DotnetBuild(ToolFactory toolFactory)
        : base(toolFactory)
    {
        FileName = "dotnet";
        Arguments = parameters => "build";
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "dotnet_build",
            description = "Build the dotnet project or solution in the current directory",
            parameters = new
            {
                type = "object",
                properties = new {},
            }
        }
    });
}
