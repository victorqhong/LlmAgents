namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;

public class ChangeDirectory : Tool
{
    private readonly IToolEventBus toolEventBus;

    private readonly string basePath;
    private readonly bool restrictToBasePath;

    public ChangeDirectory(ToolFactory toolFactory)
        : base(toolFactory)
    {
        toolEventBus = toolFactory.Resolve<IToolEventBus>();

        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;

        CurrentDirectory = basePath;
    }

    public string CurrentDirectory { get; private set; }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "directory_change",
            description = "Change the current working directory",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The directory which to change"
                    }
                },
                required = new[] { "path" }
            }
        }
    });

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        var path = parameters["path"]?.ToString();
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "path is null or empty");
            return Task.FromResult<JToken>(result);
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
                return Task.FromResult<JToken>(result);
            }

            if (!Path.Exists(path))
            {
                result.Add("error", $"path does not exist: {path}");
                return Task.FromResult<JToken>(result);
            }

            CurrentDirectory = path;

            result.Add("currentDirectory", CurrentDirectory);
            result.Add("success", true);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }

    public override void Save(string sessionId, State.StateDatabase stateDatabase)
    {
        stateDatabase.SetState(sessionId, $"{nameof(ChangeDirectory)}:{nameof(CurrentDirectory)}", CurrentDirectory);
    }

    public override void Load(string sessionId, State.StateDatabase stateDatabase)
    {
        CurrentDirectory = stateDatabase.GetSessionState(sessionId, $"{nameof(ChangeDirectory)}:{nameof(CurrentDirectory)}") ?? CurrentDirectory;
        toolEventBus.PostToolEvent(new Events.ChangeDirectoryEvent { Sender = this, Directory = CurrentDirectory });
    }
}

