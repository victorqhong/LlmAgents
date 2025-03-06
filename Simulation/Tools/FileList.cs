namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;

public class FileList
{
    private readonly string basePath;

    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "file_list",
            description = "List the files at the specified path",
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

    public FileList(string? basePath = null)
    {
        this.basePath = basePath ?? Environment.CurrentDirectory;

        Tool = new Tool
        {
            Schema = schema,
            Function = Function
        };
    }

    public Tool Tool { get; private set; }

    private JToken Function(JObject parameters)
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
            return JArray.FromObject(Directory.GetFiles(Path.Join(basePath, path)));
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}

