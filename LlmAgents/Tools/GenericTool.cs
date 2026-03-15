namespace LlmAgents.Tools;

using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

public class GenericTool : Tool
{
    private readonly Func<Session, JsonDocument, Task<JsonNode>> function;

    public GenericTool(ChatCompletionFunctionTool schema, Func<Session, JsonDocument, Task<JsonNode>> function, ToolFactory toolFactory)
        : base(toolFactory)
    {
        Schema = schema;
        this.function = function;
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; }

    public override async Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        return await function(session, parameters);
    }
}
