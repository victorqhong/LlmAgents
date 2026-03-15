namespace LlmAgents.Tools;

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class FileRead : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public FileRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;

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
            Name = "file_read",
            Description = "Read the string contents of the file at the specified path",
            Parameters = new()
            {
                Properties = new()
                {
                    { "path", new() { Type = "string", Description = "The path of the file to write" } }
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
                result.Add("error", $"files outside {basePath} can not be read");
                return Task.FromResult<JsonNode>(result);
            }

            if (!File.Exists(path))
            {
                result.Add("error", $"file at {path} does not exist or cannot be read");
                return Task.FromResult<JsonNode>(result);
            }

            var text = File.ReadAllText(path);
            result.Add("contents", text);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
