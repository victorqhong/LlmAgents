namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;

public class FileDelete : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public FileDelete(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;

        currentDirectory = basePath;

        var toolEventBus = toolFactory.Resolve<IToolEventBus>();
        toolEventBus.SubscribeToolEvent<ChangeDirectory>(OnChangeDirectory);
    }

    private Task OnChangeDirectory(ToolEvent e)
    {
        if (e is ToolCallEvent tce)
        {
            currentDirectory = tce.Result.Value<string>("currentDirectory") ?? currentDirectory;
        }
        else if (e is Events.ChangeDirectoryEvent cde)
        {
            currentDirectory = cde.Directory;
        }

        return Task.CompletedTask;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "file_delete",
            description = "Delete file at the specified path",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path of the file to delete"
                    }
                },
                required = new[] { "path" }
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var path = parameters["path"]?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return Task.FromResult<JToken>(result);
        }

        try
        {
            if (restrictToBasePath && !Path.IsPathRooted(path))
            {
                path = Path.Combine(currentDirectory, path);
            }

            path = Path.GetFullPath(path);

            if (restrictToBasePath && !path.StartsWith(basePath))
            {
                result.Add("error", $"files outside {basePath} can not be deleted");
                return Task.FromResult<JToken>(result);
            }

            if (!File.Exists(path))
            {
                result.Add("error", $"file at {path} does not exist");
                return Task.FromResult<JToken>(result);
            }

            File.Delete(path);
            result.Add("result", "succeeded");
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}
