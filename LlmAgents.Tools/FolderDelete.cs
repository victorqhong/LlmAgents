namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;

public class FolderDelete : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public FolderDelete(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;

        currentDirectory = basePath;

        toolEventBus.SubscribeToolEvent<DirectoryCurrent>(OnChangeDirectory);
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
            name = "folder_delete",
            description = "Delete a folder at the specified path",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path of the folder to delete relative to the current directory"
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
                result.Add("error", $"cannot write to files outside {basePath}");
                return Task.FromResult<JToken>(result);
            }

            if (!Directory.Exists(path))
            {
                result.Add("error", $"{path} does not exist or is not a directory. current directory is {currentDirectory}");
                return Task.FromResult<JToken>(result);
            }

            Directory.Delete(path, true);
            result.Add("result", $"folder at path deleted: {path}");
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}
