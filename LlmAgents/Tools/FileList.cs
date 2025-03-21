namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

public class FileList : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    public FileList(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "file_list",
            description = "List the files and directories at the specified path",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path to list files"
                    }
                },
                required = new[] { "path" }
            }
        }
    });

    public override JToken Function(JObject parameters)
    {
        var result = new JObject();

        var path = parameters["path"]?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return result;
        }

        try
        {
            if (restrictToBasePath && !Path.IsPathRooted(path))
            {
                path = Path.Combine(basePath, path);
            }

            path = Path.GetFullPath(path);

            if (restrictToBasePath && !path.StartsWith(basePath))
            {
                result.Add("error", $"cannot list files outside {basePath}");
                return result;
            }

            var files = Directory.GetFiles(path);
            var directories = Directory.GetDirectories(path);
            return JArray.FromObject(directories.Concat(files));
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}

