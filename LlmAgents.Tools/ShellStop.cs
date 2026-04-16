namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class ShellStop : ShellToolBase
{
    public ShellStop(ToolFactory toolFactory) : base(toolFactory) { }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "shell_stop",
            Description = "Stop shell session and release resources.",
            Parameters = new()
            {
                Properties = new() { },
                Required = []
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        return manager.StopAsync(session);
    }
}
