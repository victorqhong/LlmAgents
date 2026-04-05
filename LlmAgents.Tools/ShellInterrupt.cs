namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class ShellInterrupt : ShellToolBase
{
    public ShellInterrupt(ToolFactory toolFactory) : base(toolFactory) { }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "shell_interrupt",
            Description = "Send Ctrl+C to shell session and verify responsiveness.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "timeout_ms", new() { Type = "integer", Description = "Probe timeout in milliseconds." } }
                },
                Required = []
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        parameters.TryGetValueInt("timeout_ms", out var timeoutMs);
        return manager.InterruptAsync(session, timeoutMs);
    }
}
