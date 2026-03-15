namespace LlmAgents.Tools;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class NumberLines : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public NumberLines(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out bool restrict) ? restrict : true;

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
            Name = "number_lines",
            Description = "Reads a text file and returns its contents with line numbers prepended to each line. Use this tool to accurately generate diffs.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "path", new() { Type = "string", Description = "The path of the file to read" } }
                },
                Required = ["path"]
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

        try
        {
            if (restrictToBasePath && !Path.IsPathRooted(path))
            {
                path = Path.Combine(currentDirectory, path);
            }

            path = Path.GetFullPath(path);

            if (restrictToBasePath && !path.StartsWith(basePath))
            {
                result.Add("error", $"File outside {basePath} cannot be read");
                return Task.FromResult<JsonNode>(result);
            }

            if (!File.Exists(path))
            {
                result.Add("error", $"File not found: {path}");
                return Task.FromResult<JsonNode>(result);
            }

            List<string> lines = File.ReadAllLines(path).ToList();
            List<string> numberedLines = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                numberedLines.Add($"{i + 1}: {lines[i]}");
            }

            string resultContent = string.Join("\n", numberedLines);
            result.Add("contents", resultContent);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
