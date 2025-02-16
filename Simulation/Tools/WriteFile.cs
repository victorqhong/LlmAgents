namespace Simulation.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

public class WriteFile
{
    public static Newtonsoft.Json.Linq.JObject Definition;

    public static System.Func<JObject, JObject> Function = (parameters) =>
    {
        var contents = parameters["contents"]?.ToString();
        if (string.IsNullOrEmpty(contents))
        {
            return new JObject();
        }

        var path = parameters["path"]?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            return new JObject();
        }


        var result = new JObject();

        try
        {
            System.IO.File.WriteAllText(path, contents);
            result.Add("exception", string.Empty);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    };

    static WriteFile()
    {
        Definition = Newtonsoft.Json.Linq.JObject.FromObject(new
        {
            type = "function",
            function = new
            {
                name = "write_file",
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
    }
}

