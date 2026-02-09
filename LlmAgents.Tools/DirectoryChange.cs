namespace LlmAgents.Tools;

using LlmAgents.State;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

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

    public override Task<JToken> Function(Session session, JObject parameters)
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

    public override void Save(Session session, StateDatabase stateDatabase)
    {
        stateDatabase.SetState(session.SessionId, $"{nameof(DirectoryChange)}:{nameof(CurrentDirectory)}", CurrentDirectory);
    }

    public override void Load(Session session, StateDatabase stateDatabase)
    {
        CurrentDirectory = stateDatabase.GetSessionState(session.SessionId, $"{nameof(DirectoryChange)}:{nameof(CurrentDirectory)}") ?? CurrentDirectory;
        toolEventBus?.PostToolEvent(new Events.ChangeDirectoryEvent { Sender = this, Directory = CurrentDirectory });
    }
}

