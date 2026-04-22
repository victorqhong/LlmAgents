namespace LlmAgents.Tools;

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class FileList : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public FileList(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out var restrict) ? restrict : true;

        currentDirectory = basePath;

        var toolEventBus = toolFactory.ResolveWithDefault<IToolEventBus>();
        toolEventBus?.SubscribeToolEvent<DirectoryChange>(OnChangeDirectory);
    }

    private Task OnChangeDirectory(ToolEvent e)
    {
        if (e is ToolCallEvent tce && tce.Result.AsObject() is JsonObject jsonObject && jsonObject.TryGetPropertyValue("currentDirectory", out var property))
        {
            currentDirectory = property?.GetValue<string>() ?? currentDirectory;
        }
        else if (e is Events.ChangeDirectoryEvent cde)
        {
            currentDirectory = cde.Directory;
        }

        return Task.CompletedTask;
    }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "file_list",
            Description = "List the files and directories at the specified path",
            Parameters = new()
            {
                Properties = new()
                {
                    { "path", new() { Type = "string", Description = "The path to list files"  } },
                    { "recursive", new() { Type = "boolean", Description = "Whether to recursively list files in all subdirectories (default is false and must specify search pattern)"  } },
                    { "searchPattern", new() { Type = "string", Description = "The search string to match against the names of files in path (default *.*). This parameter can contain a combination of valid literal path and wildcard (* and ?) characters, but it doesn't support regular expressions."  } }
                },
                Required = [ "path" ]
            }
        }
    };

    public override Task<JsonNode> Function(Session session, JsonDocument parameters)
    {
        var result = new JsonObject();

        if (!parameters.TryGetValueString("path", string.Empty, out var path) || string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return Task.FromResult<JsonNode>(result);
        }

        parameters.TryGetValueString("searchPattern", "*.*", out var searchPattern);
        parameters.TryGetValueBool("recursive", false, out var recursive);

        try
        {
            if (restrictToBasePath && !Path.IsPathRooted(path))
            {
                path = Path.Combine(currentDirectory, path);
            }

            path = Path.GetFullPath(path);

            if (Directory.Exists(path) && !Path.EndsInDirectorySeparator(path))
            {
                path += Path.DirectorySeparatorChar;
            }

            if (restrictToBasePath && !path.StartsWith(basePath))
            {
                result.Add("error", $"cannot list files outside {basePath}");
                return Task.FromResult<JsonNode>(result);
            }

            var searchOption = SearchOption.TopDirectoryOnly;
            if (recursive && !string.Equals(searchPattern, "*.*"))
            {
                searchOption = SearchOption.AllDirectories;
            }

            var files = Directory.GetFiles(path, searchPattern, searchOption);
            var directories = Directory.GetDirectories(path, searchPattern, searchOption);

            var directoryContents = directories.Concat(files);

            var node = JsonSerializer.SerializeToNode(directoryContents);
            if (node == null)
            {
                result.Add("error", $"error with tool: could not serialize results");
                return Task.FromResult<JsonNode>(result);
            }

            return Task.FromResult(node);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
