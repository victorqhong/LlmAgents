namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;

public class DirectoryCurrent : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public DirectoryCurrent(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;

        currentDirectory = basePath;

        toolEventBus.SubscribeToolEvent<DirectoryCurrent>(OnChangeDirectory);
    }

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "directory_current",
            description = "Get the current working directory",
            parameters = new
            {
                type = "object",
                properties = new
                {
                }
            }
        }
    });

    private Task OnChangeDirectory(ToolEvent e)
    {
        if (e is ToolCallEvent tce)
        {
            currentDirectory = tce.Result.Value<string>("currentDirectory") ?? currentDirectory;
        }
        else if (e is Events.ChangeDirectoryEvent cde)
        {
            currentDirectory = cde.Directory;
        }

        return Task.CompletedTask;
    }

    public override Task<JToken> Function(JObject parameters)
    {
        var result = new JObject();

        try
        {
            result.Add("currentDirectory", currentDirectory);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }
}