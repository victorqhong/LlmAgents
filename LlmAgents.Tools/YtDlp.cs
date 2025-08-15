namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;

public class YtDlp : Tool
{
    private readonly string workingDirectory;

    public YtDlp(ToolFactory toolFactory)
        : base(toolFactory)
    {
        workingDirectory = toolFactory.GetParameter("basePath") ?? Environment.CurrentDirectory;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "ytdlp_audio_extract",
            description = "Extract the audio of a YouTube video with yt-dlp",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    videoUrl = new
                    {
                        type = "string",
                        description = "URL of the video to extract audio"
                    }
                },
                required = new[] { "videoUrl" }
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var videoUrl = parameters["videoUrl"]?.ToString();
        if (string.IsNullOrEmpty(videoUrl))
        {
            result.Add("error", "videoUrl parameter is null or empty");
            return Task.FromResult<JToken>(result);
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

        return Task.FromResult<JToken>(result);
    }
}
