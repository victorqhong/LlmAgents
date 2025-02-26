namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;

public class FileRead
{
    private readonly string basePath;

    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "file_read",
            description = "Read the string contents of the file at the specified path",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path of the file to write"
                    }
                },
                required = new[] { "path" }
            }
        }
    });

    public FileRead(string? basePath = null)
    {
        this.basePath = basePath ?? Environment.CurrentDirectory;

        Tool = new Tool
        {
            Schema = schema,
            Function = Function
        };
    }

    public Tool Tool { get; private set; }

    private JObject Function(JObject parameters)
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
            var text = File.ReadAllText(Path.Join(basePath, path));
            result.Add("contents", text);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }
}
