namespace LlmAgents.Tools;

using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class YtDlp : Tool
{
    private readonly string workingDirectory;

    public YtDlp(ToolFactory toolFactory)
        : base(toolFactory)
    {
        workingDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "ytdlp_audio_extract",
            Description = "Extract the audio of a YouTube video with yt-dlp",
            Parameters = new() 
            {
                Properties = new() 
                {
                    { "videoUrl", new() { Type = "string", Description = "URL of the video to extract audio" } }
                },
                Required = ["videoUrl"]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("videoUrl", string.Empty, out var videoUrl) || string.IsNullOrEmpty(videoUrl))
        {
            result.Add("error", "videoUrl parameter is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        try
        {
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "yt-dlp";
            process.StartInfo.Arguments = $"-x --audio-format mp3 --audio-quality 0 {videoUrl}";
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit(60 * 1000);

            if (!process.HasExited)
            {
                result.Add("warning", "yt-dlp did not exit after 60 seconds and command may have failed");
                process.Kill();
            }
            else
            {
                result.Add("exitcode", process.ExitCode);
            }

            result.Add("stdout", process.StandardOutput.ReadToEnd());
            result.Add("stderr", process.StandardError.ReadToEnd());
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
