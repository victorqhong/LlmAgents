using Newtonsoft.Json.Linq;

namespace LlmAgents.Tools;

public class DotnetDevelop : Tool
{
    string workingDirectory;

    public DotnetDevelop(ToolFactory toolFactory)
        : base(toolFactory)
    {
        workingDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "dotnet_develop",
            description = "Build, test, and run the dotnet project or solution in the current directory",
            parameters = new
            {
                type = "object",
                properties = new {},
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "pwsh";
            process.StartInfo.Arguments = "-c \"& { dotnet build; dotnet run }\"";
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit(10_000);

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
