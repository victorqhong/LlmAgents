namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.State;

public abstract class Command : Tool
{
    private readonly string workingDirectory;

    public Command(ToolFactory toolFactory)
        : base(toolFactory)
    {
        workingDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;
    }

    public required string FileName { get; set; }

    public required Func<JsonDocument, string?> Arguments { get; set; }

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        var arguments = Arguments(parameters);
        if (arguments == null)
        {
            result.Add("error", "arguments is null");
            return Task.FromResult<JsonNode>(result);
        }

        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = FileName;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit();

            result.Add("stdout", process.StandardOutput.ReadToEnd());
            result.Add("stderr", process.StandardError.ReadToEnd());
            result.Add("exitcode", process.ExitCode);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
