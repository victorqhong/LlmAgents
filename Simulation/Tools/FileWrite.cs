namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;

public class FileWrite
{
    private JObject schema = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "file_write",
            description = "Write the string contents to the file at the specified path",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    contents = new
                    {
                        type = "string",
                        description = "The string contents to write"
                    },
                    path = new
                    {
                        type = "string",
                        description = "The path of the file to write"
                    }
                },
                required = new[] { "contents", "path" }
            }
        }
    });

    public FileWrite()
    {
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

        var contents = parameters["contents"]?.ToString();
        if (string.IsNullOrEmpty(contents))
        {
            result.Add("error", "contents is null or empty");
            return result;
        }

        var path = parameters["path"]?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return result;
        }

        try
        {
            System.IO.File.WriteAllText(path, contents);
            result.Add("result", "success");
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;

    }
}
