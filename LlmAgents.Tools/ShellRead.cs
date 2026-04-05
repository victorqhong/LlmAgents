namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class ShellRead : ShellToolBase
{
    public ShellRead(ToolFactory toolFactory) : base(toolFactory) { }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "shell_read",
            Description = "Read shell output from a cursor. Returns output chunk and next_cursor.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "cursor", new() { Type = "integer", Description = "Cursor to read from; defaults to beginning of current buffer." } },
                    { "max_chars", new() { Type = "integer", Description = "Maximum characters to return." } }
                },
                Required = []
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        parameters.TryGetValueInt("cursor", out var cursor);
        parameters.TryGetValueInt("max_chars", out var maxChars);
        return Task.FromResult(manager.Read(session, cursor, maxChars));
    }
}
