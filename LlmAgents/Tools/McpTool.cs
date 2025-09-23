namespace LlmAgents.Tools;

using ModelContextProtocol.Client;
using Newtonsoft.Json.Linq;

public class McpTool : Tool
{
    private readonly McpClientTool mcpClientTool;

    private readonly IMcpClient mcpClient;

    public McpTool(McpClientTool mcpClientTool, IMcpClient mcpClient)
        : base(null)
    {
        this.mcpClientTool = mcpClientTool;
        this.mcpClient = mcpClient;

        Schema = new JObject
        {
            { "type", "function" },
            { "function", new JObject()
                {
                    { "name", mcpClientTool.Name },
                    { "description", mcpClientTool.Description },
                    { "parameters", JObject.Parse(mcpClientTool.JsonSchema.ToString()) }
                }
            }
        };
    }

    public override JObject Schema { get; protected set; }

    public override async Task<JToken> Function(JObject parameters)
    {
        var arguments = parameters.ToObject<IReadOnlyDictionary<string, object?>>();
        var toolCallResult = await mcpClient.CallToolAsync(mcpClientTool.Name, arguments);

        if (toolCallResult.StructuredContent != null)
        {
            return JToken.Parse(toolCallResult.StructuredContent.ToString());
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}
