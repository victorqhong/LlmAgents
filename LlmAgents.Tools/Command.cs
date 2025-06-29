namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;

public abstract class Command : Tool
{
    private readonly string workingDirectory;

    public Command(ToolFactory toolFactory)
        : base(toolFactory)
    {
        workingDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;
    }

    public required string FileName { get; set; }

    public required Func<JObject, string?> Arguments { get; set; }

    public override async Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var arguments = Arguments(parameters);
        if (arguments == null)
        {
            result.Add("error", "arguments is null");
            return result;
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

        return result;
    }
}
