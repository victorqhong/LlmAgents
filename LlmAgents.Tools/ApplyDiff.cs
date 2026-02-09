namespace LlmAgents.Tools;

using LlmAgents.State;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public class ApplyDiff : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public ApplyDiff(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out bool restrict) ? restrict : true;

        currentDirectory = basePath;

        var toolEventBus = toolFactory.Resolve<IToolEventBus>();
        toolEventBus.SubscribeToolEvent<DirectoryChange>(OnChangeDirectory);
    }

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

    public override JObject Schema { get; protected set; } = JObject.FromObject(new
    {
        type = "function",
        function = new
        {
            name = "apply_diff",
            description = "Applies the provided unified diff contents to a file and modifies it in-place. Use the 'number_lines' tool to generate line numbers for an accurate diff.",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path of the file to modify in-place"
                    },
                    diffContent = new
                    {
                        type = "string",
                        description = "The contents of the unified diff (patch) to apply. Ensure hunk headers are included."
                    }
                },
                required = new[] { "path", "diffContent" }
            }
        }
    });

    public override Task<JToken> Function(Session session, JObject parameters)
    {
        var result = new JObject();

        var path = parameters["path"]?.ToString();
        var diffContent = parameters["diffContent"]?.ToString();

        // Validate input parameters
        if (string.IsNullOrEmpty(path))
        {
            result.Add("error", "Path is null or empty");
            return Task.FromResult<JToken>(result);
        }
        if (string.IsNullOrEmpty(diffContent))
        {
            result.Add("error", "Diff content is null or empty");
            return Task.FromResult<JToken>(result);
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
                result.Add("error", $"File outside {basePath} cannot be modified");
                return Task.FromResult<JToken>(result);
            }

            // Check if the file exists
            if (!File.Exists(path))
            {
                result.Add("error", $"File not found: {path}");
                return Task.FromResult<JToken>(result);
            }

            // Read the original file
            List<string> originalLines = File.ReadAllLines(path).ToList();
            // Split diff content into lines
            List<string> diffLines = diffContent.Split(new[] { "\n", "\r\n" }, StringSplitOptions.None).ToList();

            // Apply the diff
            List<string> modifiedLines = Apply(originalLines, diffLines);

            // Write the result back to the same file (in-place)
            File.WriteAllLines(path, modifiedLines);
            result.Add("success", $"Diff applied successfully! File modified in-place at {path}");
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }

    private List<string> Apply(List<string> original, List<string> diff)
    {
        List<string> result = new List<string>();
        int currentLine = 0;
        bool inHunk = false;
        int originalStart = 0;
        int originalLength = 0;

        foreach (string line in diff)
        {
            // Check for hunk header (e.g., @@ -1,5 +1,6 @@)
            if (line.StartsWith("@@"))
            {
                inHunk = true;
                try
                {
                    string[] parts = line.Split(' ');
                    if (parts.Length < 2)
                    {
                        throw new FormatException("Invalid hunk header format in diff");
                    }
                    string originalRange = parts[1]; // e.g., "-1,5"
                    string[] rangeParts = originalRange.Split(',');
                    if (!int.TryParse(rangeParts[0].Substring(1), out originalStart))
                    {
                        throw new FormatException("Failed to parse starting line in hunk header");
                    }
                    originalStart -= 1; // Convert to 0-based index
                    originalLength = rangeParts.Length > 1 && int.TryParse(rangeParts[1].TrimEnd(']'), out int length) ? length : 1;
                }
                catch (Exception e)
                {
                    throw new FormatException($"Error parsing hunk header '{line}': {e.Message}");
                }
                // Copy lines before the hunk if needed
                while (currentLine < originalStart)
                {
                    if (currentLine < original.Count)
                    {
                        result.Add(original[currentLine]);
                    }
                    currentLine++;
                }
                continue;
            }

            if (!inHunk) continue; // Skip non-hunk lines (e.g., ---, +++)

            // Process the hunk
            if (line.StartsWith("-"))
            {
                // Line removed, skip it in original
                currentLine++;
            }
            else if (line.StartsWith("+"))
            {
                // Line added, include it in result
                result.Add(line.Substring(1)); // Remove the '+' prefix
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                // Context line, keep it and move forward
                result.Add(line.TrimStart());
                currentLine++;
            }
            else
            {
                // Handle empty lines in diff (context or literal empty line)
                if (currentLine < original.Count && string.IsNullOrWhiteSpace(original[currentLine]))
                {
                    result.Add(original[currentLine]);
                    currentLine++;
                }
                else
                {
                    result.Add(line); // Add empty line from diff
                }
            }
        }

        // Copy any remaining lines from the original file
        while (currentLine < original.Count)
        {
            result.Add(original[currentLine]);
            currentLine++;
        }

        return result;
    }
}
