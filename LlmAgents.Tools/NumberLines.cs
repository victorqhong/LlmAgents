﻿namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class NumberLines : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    public NumberLines(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out bool restrict) ? restrict : true;
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "number_lines",
            description = "Reads a text file and returns its contents with line numbers prepended to each line. Use this tool to accurately generate diffs.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path of the file to read"
                    }
                },
                required = new[] { "path" }
            }
        }
    });

    public override async Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var path = parameters["path"]?.ToString();

        // Validate input parameter
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "Path is null or empty");
            return result;
        }

        try
        {
            // Resolve and validate file path
            string resolvedPath = ResolvePath(path);

            if (restrictToBasePath && !resolvedPath.StartsWith(basePath))
            {
                result.Add("error", $"File outside {basePath} cannot be read");
                return result;
            }

            // Check if the file exists
            if (!File.Exists(resolvedPath))
            {
                result.Add("error", $"File not found: {resolvedPath}");
                return result;
            }

            // Read the file and prepend line numbers
            List<string> lines = File.ReadAllLines(resolvedPath).ToList();
            List<string> numberedLines = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                numberedLines.Add($"{i + 1}: {lines[i]}");
            }

            // Join the lines into a single string for the result
            string resultContent = string.Join("\n", numberedLines);
            result.Add("contents", resultContent);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return result;
    }

    private string ResolvePath(string path)
    {
        if (restrictToBasePath && !Path.IsPathRooted(path))
        {
            return Path.GetFullPath(Path.Combine(basePath, path));
        }
        return Path.GetFullPath(path);
    }
}