using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class DotnetRun : Command
{
    public DotnetRun(ToolFactory toolFactory)
        : base(toolFactory)
    {
        FileName = "dotnet";
        Arguments = parameters => "run";
        timeoutMs = 10_000;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "dotnet_run",
            description = "Run the dotnet project in the current directory",
            parameters = new
            {
                type = "object",
                properties = new {},
            }
        }
    });
}
