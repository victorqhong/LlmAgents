namespace LlmAgents.Tools;

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class DirectoryChange : Tool
{
    private readonly IToolEventBus? toolEventBus;

    private readonly string basePath;
    private readonly bool restrictToBasePath;

    public DirectoryChange(ToolFactory toolFactory)
        : base(toolFactory)
    {
        toolEventBus = toolFactory.ResolveWithDefault<IToolEventBus>();

        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;

        CurrentDirectory = basePath;
    }

    public string CurrentDirectory { get; private set; }

    public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
    {
        Function = new()
        {
            Name = "directory_change",
            Description = "Change the current working directory",
            Parameters = new()
            {
                Properties = new()
                {
                    { "path", new() { Type = "string", Description = "The directory which to change" } }
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
                path = Path.Combine(CurrentDirectory, path);
            }

            path = Path.GetFullPath(path);

            if (restrictToBasePath && !path.StartsWith(basePath))
            {
                result.Add("error", $"cannot change to directory outside of {basePath}");
                return Task.FromResult<JsonNode>(result);
            }

            if (!Path.Exists(path))
            {
                result.Add("error", $"path does not exist: {path}");
                return Task.FromResult<JsonNode>(result);
            }

            CurrentDirectory = path;

            result.Add("currentDirectory", CurrentDirectory);
            result.Add("success", true);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }

    public async override Task Save(Session session)
    {
        await session.SetState($"{nameof(DirectoryChange)}:{nameof(CurrentDirectory)}", CurrentDirectory);
    }

    public async override Task Load(Session session)
    {
        CurrentDirectory = await session.GetState($"{nameof(DirectoryChange)}:{nameof(CurrentDirectory)}") ?? CurrentDirectory;
        toolEventBus?.PostToolEvent(new Events.ChangeDirectoryEvent { Sender = this, Directory = CurrentDirectory });
    }
}

