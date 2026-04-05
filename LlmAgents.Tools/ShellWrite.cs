namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class ShellWrite : ShellToolBase
{
    public ShellWrite(ToolFactory toolFactory) : base(toolFactory) { }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "shell_write",
            Description = "Write data to shell stdin.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "input", new() { Type = "string", Description = "Input text to write." } },
                    { "append_newline", new() { Type = "boolean", Description = "Append newline after input." } }
                },
                Required = ["input"]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        if (!parameters.TryGetValueString("input", string.Empty, out var input))
        {
            return Task.FromResult<JsonNode>(new JsonObject
            {
                { "error", "input is required" }
            });
        }

        parameters.TryGetValueBool("append_newline", false, out var appendNewline);
        return manager.WriteAsync(session, input, appendNewline);
    }
}
