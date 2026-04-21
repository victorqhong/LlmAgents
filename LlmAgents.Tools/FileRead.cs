namespace LlmAgents.Tools;

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LlmAgents.Extensions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;

public class FileRead : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;
    private readonly int defaultMaxBytes;
    private readonly int maxAllowedMaxBytes;

    private string currentDirectory;

    public FileRead(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;
        defaultMaxBytes = int.TryParse(toolFactory.GetParameter(nameof(defaultMaxBytes)), out var defaultVal) ? defaultVal : 10000;
        maxAllowedMaxBytes = int.TryParse(toolFactory.GetParameter(nameof(maxAllowedMaxBytes)), out var maxVal) ? maxVal : 50000;

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
            Description = "Read the string contents of the file at the specified path. Supports chunked reading with cursor and max_bytes parameters for large files.",
            Parameters = new()
            {
                Properties = new()
                {
                    { "path", new() { Type = "string", Description = "The path of the file to read" } },
                    { "cursor", new() { Type = "integer", Description = "Starting byte position in the file (default: 0). Use the returned cursor value to read the next chunk." } },
                    { "max_bytes", new() { Type = "integer", Description = "Maximum number of bytes to read per chunk (default: 10000, max: 50000)" } }
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

            // Parse optional parameters
            parameters.TryGetValueInt("cursor", out var cursorNullable);
            parameters.TryGetValueInt("max_bytes", out var maxBytesNullable);

            int cursor = cursorNullable ?? 0;
            int maxBytes = maxBytesNullable ?? defaultMaxBytes;

            // Clamp max_bytes to allowed range
            maxBytes = Math.Clamp(maxBytes, 1, maxAllowedMaxBytes);

            // Ensure cursor is non-negative
            if (cursor < 0)
            {
                cursor = 0;
            }

            var fileInfo = new FileInfo(path);
            var fileLength = (int)fileInfo.Length;

            // If cursor is past EOF, return EOF indication
            if (cursor >= fileLength)
            {
                result.Add("contents", string.Empty);
                result.Add("cursor", cursor);
                result.Add("eof", true);
                result.Add("bytes_read", 0);
                result.Add("file_length", fileLength);
                return Task.FromResult<JsonNode>(result);
            }

            // Calculate bytes to read
            int bytesToRead = Math.Min(maxBytes, fileLength - cursor);
            var buffer = new byte[bytesToRead];

            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            fileStream.Position = cursor;

            int bytesRead = fileStream.Read(buffer, 0, bytesToRead);
            int newCursor = (int)fileStream.Position;

            // Convert bytes to string
            string text = bytesRead > 0 ? Encoding.UTF8.GetString(buffer, 0, bytesRead) : string.Empty;

            bool eof = newCursor >= fileLength;

            result.Add("contents", text);
            result.Add("cursor", newCursor);
            result.Add("eof", eof);
            result.Add("bytes_read", bytesRead);
            result.Add("file_length", fileLength);
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JsonNode>(result);
    }
}
