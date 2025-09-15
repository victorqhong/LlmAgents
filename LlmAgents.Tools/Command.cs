namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;

public abstract class Command : Tool
{
    private string workingDirectory;

    protected int timeoutMs = 10_000;

    public Command(ToolFactory toolFactory)
        : base(toolFactory)
    {
        workingDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;

        toolEventBus.SubscribeToolEvent<DirectoryCurrent>(OnChangeDirectory);
    }

    public required string FileName { get; set; }

    public required Func<JObject, string?> Arguments { get; set; }

    private Task OnChangeDirectory(ToolEvent e)
    {
        if (e is ToolCallEvent tce)
        {
            workingDirectory = tce.Result.Value<string>("currentDirectory") ?? workingDirectory;
        }
        else if (e is Events.ChangeDirectoryEvent cde)
        {
            workingDirectory = cde.Directory;
        }

        return Task.CompletedTask;
    }

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var arguments = Arguments(parameters);
        if (arguments == null)
        {
            result.Add("error", "arguments is null");
            return Task.FromResult<JToken>(result);
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
            process.WaitForExit(timeoutMs);

            result.Add("stdout", process.StandardOutput.ReadToEnd());
            result.Add("stderr", process.StandardError.ReadToEnd());
            result.Add("exitcode", process.ExitCode);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}
