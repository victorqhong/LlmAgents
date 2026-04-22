namespace LlmAgents.Tools;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

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

        if (toolCallResult == null)
        {
            var jsonObject = new JsonObject
            {
                ["error"] = "Did not get response from remote McpTool"
            };

            return jsonObject;
        }
        else if (toolCallResult.IsError != null && toolCallResult.IsError.HasValue && toolCallResult.IsError.Value)
        {
            var sb = new StringBuilder();
            foreach (var item in toolCallResult.Content)
            {
                if (string.Equals("text", item.Type) && item is TextContentBlock textContentBlock)
                {
                    sb.AppendLine(textContentBlock.Text);
                }
            }

            if (sb.Length == 0)
            {
                sb.Append("Remote McpTool returned an error, but no text content was found in the response.");
            }

            var jsonObject = new JsonObject
            {
                ["error"] = sb.ToString()
            };

            return jsonObject;
        }
        else if (toolCallResult.StructuredContent != null)
        {
            return toolCallResult.StructuredContent;
        }
        else if (toolCallResult.Content.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var content in toolCallResult.Content)
            {
                if (string.Equals(content.Type, "text") && content is TextContentBlock textContent)
                {
                    sb.AppendLine(textContent.Text);
                }
                else
                {
                    throw new NotImplementedException($"Tool call result content type not supported: {content.Type}");
                }
            }

            return new JsonObject
            {
                ["content"] = sb.ToString()
            };
        }
        else
        {
            throw new NotImplementedException("Tool call result unhandled");
        }
    }
}
