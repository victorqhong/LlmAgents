using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class DotnetTest : Command
{
    public DotnetTest(ToolFactory toolFactory)
        : base(toolFactory)
    {
        FileName = "dotnet";
        Arguments = parameters => "test";
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "dotnet_test",
            description = "Test the dotnet project or solution in the current directory",
            parameters = new
            {
                type = "object",
                properties = new {},
            }
        }
    });
}
