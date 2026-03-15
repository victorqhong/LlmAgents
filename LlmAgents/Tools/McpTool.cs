namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using ModelContextProtocol.Client;

public class McpTool : Tool
{
    private readonly McpClientTool mcpClientTool;

    private readonly IMcpClient mcpClient;

    public McpTool(McpClientTool mcpClientTool, IMcpClient mcpClient, ToolFactory toolFactory)
        : base(toolFactory)
    {
        this.mcpClientTool = mcpClientTool;
        this.mcpClient = mcpClient;

        Schema = new ChatCompletionFunctionTool
        {
            Type = "function",
            Function = new ChatCompletionFunctionDefinition
            {
                Name = mcpClientTool.Name,
                Description = mcpClientTool.Description,
                Parameters = mcpClientTool.JsonSchema.Deserialize<ChatCompletionFunctionParameters>()
            }
        };
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; }

    public override async Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var arguments = parameters.Deserialize<IReadOnlyDictionary<string, object?>>();
        var toolCallResult = await mcpClient.CallToolAsync(mcpClientTool.Name, arguments);

        if (toolCallResult.StructuredContent != null)
        {
            return toolCallResult.StructuredContent;
        }
        else
        {
            throw new NotImplementedException();
        }
    }
}
