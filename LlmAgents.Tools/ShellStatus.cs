namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class ShellStatus : ShellToolBase
{
    public ShellStatus(ToolFactory toolFactory) : base(toolFactory) { }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "shell_status",
            Description = "Get shell session status and output buffer cursor range.",
            Parameters = new()
            {
                Properties = new() { },
                Required = []
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        return Task.FromResult(manager.Status(session));
    }
}
