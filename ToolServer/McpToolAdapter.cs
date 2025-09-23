﻿using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToolServer;

public class McpToolAdapter : McpServerTool
{
    private readonly LlmAgents.Tools.Tool tool;

    private readonly Tool protocolTool;

    public McpToolAdapter(LlmAgents.Tools.Tool tool)
    {
        this.tool = tool;

        var schemaDocument = JsonDocument.Parse(tool.Schema.ToString());

        var function = schemaDocument.RootElement.GetProperty("function");
        var description = function.GetProperty("description").GetString();
        var inputSchema = function.GetProperty("parameters");

        protocolTool = new Tool()
        {
            Name = tool.Name,
            Description = description,
            InputSchema = inputSchema
        };
    }

    public override Tool ProtocolTool => protocolTool;

    public async override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default)
    {
        var result = new CallToolResult();
        if (request.Params == null)
        {
            result.IsError = true;
            result.Content.Add(new TextContentBlock()
            {
                Text = "Params is null"
            });

            return result;
        }

        try
        {
            var arguments = JObject.Parse(JsonSerializer.Serialize(request.Params.Arguments));
            var toolResult = await tool.Function(arguments);
            var toolResultJson = JsonDocument.Parse(toolResult.ToString());

            result.StructuredContent = JsonNode.Parse(JsonSerializer.Serialize(toolResultJson.RootElement));
        }
        catch (Exception e)
        {
            result.IsError = true;
            result.Content.Add(new TextContentBlock()
            {
                Text = $"Got exception: {e.Message}"
            });
        }

        return result;
    }
}