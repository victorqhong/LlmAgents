namespace LlmAgents.Tools;

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class ShellExec : ShellToolBase
{
    private static string shellName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PowerShell Core" : "bash";

    public ShellExec(ToolFactory toolFactory) : base(toolFactory) { }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "shell_exec",
            Description = $"Run a command in an interactive {shellName} session. Session auto-starts if needed.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "command", new() { Type = "string", Description = "Shell command and arguments to run." } },
                    { "wait_for_exit", new() { Type = "boolean", Description = "When true, wait for completion by sentinel." } },
                    { "timeout_ms", new() { Type = "integer", Description = "Wait timeout in milliseconds when wait_for_exit is true." } }
                },
                Required = ["command"]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        if (!parameters.TryGetValueString("command", string.Empty, out var command) || string.IsNullOrWhiteSpace(command))
        {
            return Task.FromResult<JsonNode>(new JsonObject
            {
                { "error", "command is null or empty" }
            });
        }

        parameters.TryGetValueBool("wait_for_exit", true, out var waitForExit);
        parameters.TryGetValueInt("timeout_ms", out var timeoutMs);
        return manager.ExecAsync(session, command, waitForExit, timeoutMs);
    }
}
