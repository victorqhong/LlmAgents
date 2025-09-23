namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

public class FileList : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    private string currentDirectory;

    public FileList(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out var restrict) ? restrict : true;

        currentDirectory = basePath;

        var toolEventBus = toolFactory.Resolve<IToolEventBus>();
        toolEventBus.SubscribeToolEvent<ChangeDirectory>(OnChangeDirectory);
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
            name = "file_list",
            description = "List the files and directories at the specified path",
            parameters = new
            {
                type = "object",
                properties = new
                {
                    path = new
                    {
                        type = "string",
                        description = "The path to list files"
                    },
                    recursive = new
                    {
                        type = "boolean",
                        description = "Whether to recursively list files in all subdirectories (default is false)"
                    },
                    searchPattern = new
                    {
                        type = "string",
                        description = "The search string to match against the names of files in path (default *.*). This parameter can contain a combination of valid literal path and wildcard (* and ?) characters, but it doesn't support regular expressions."
                    },
                    useIgnoreFile = new
                    {
                        type = "boolean",
                        description = "Whether to use .gitignore and .llmignore files to filter results (default is true)"
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

        var recursive = parameters["recursive"]?.Value<bool>() ?? false;
        var searchPattern = parameters["searchPattern"]?.Value<string>() ?? "*.*";
        var useIgnoreFile = parameters["useIgnoreFile"]?.Value<bool>() ?? true;

        try
        {
            if (restrictToBasePath && !Path.IsPathRooted(path))
            {
                path = Path.Combine(currentDirectory, path);
            }

            path = Path.GetFullPath(path);

            if (Directory.Exists(path) && !Path.EndsInDirectorySeparator(path))
            {
                path += Path.DirectorySeparatorChar;
            }

            if (restrictToBasePath && !path.StartsWith(basePath))
            {
                result.Add("error", $"cannot list files outside {basePath}");
                return Task.FromResult<JToken>(result);
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(path, searchPattern, searchOption);
            var directories = Directory.GetDirectories(path, searchPattern, searchOption);

            var directoryContents = directories.Concat(files);

            if (useIgnoreFile)
            {
                var filter = new MultiLevelGitIgnoreFilter(basePath);
                directoryContents = directoryContents.Where(filter.IsNotIgnored).ToArray();
            }

            return Task.FromResult<JToken>(JArray.FromObject(directoryContents));
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
    }

    private class MultiLevelGitIgnoreFilter
    {
        private readonly List<(string directory, GitIgnoreFilter filter)> filters = new();

        public MultiLevelGitIgnoreFilter(string basePath)
        {
            var gitIgnoreFiles = Directory.GetFiles(basePath, ".gitignore", SearchOption.AllDirectories);
            foreach (var file in gitIgnoreFiles)
            {
                var directory = new DirectoryInfo(file);
                if (directory.Parent == null)
                {
                    continue;
                }

                var filter = new GitIgnoreFilter(file);
                filters.Add((directory.Parent.FullName, filter));
            }

            var llmIgnoreFiles = Directory.GetFiles(basePath, ".llmignore", SearchOption.AllDirectories);
            foreach (var file in llmIgnoreFiles)
            {
                var directory = new DirectoryInfo(file);
                if (directory.Parent == null)
                {
                    continue;
                }

                var filter = new GitIgnoreFilter(file);
                filters.Add((directory.Parent.FullName, filter));
            }
        }

        public bool IsNotIgnored(string path)
        {
            var isIgnored = false;
            foreach (var filter in filters)
            {
                if (path.StartsWith(filter.Item1))
                {
                    isIgnored = filter.Item2.IsIgnored(Path.GetRelativePath(filter.Item1, path));
                    if (isIgnored)
                    {
                        break;
                    }
                }
            }

            return !isIgnored;
        }
    }

    private class GitIgnoreFilter
    {
        private readonly List<Regex> ignorePatterns = new List<Regex>();

        public GitIgnoreFilter(string gitIgnoreFilePath, params string[] additionalPatterns)
        {
            if (!File.Exists(gitIgnoreFilePath))
                throw new FileNotFoundException("The .gitignore file was not found.", gitIgnoreFilePath);

            var lines = File.ReadAllLines(gitIgnoreFilePath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("!"))
                    continue;

                var pattern = line.Trim();
                if (string.IsNullOrEmpty(pattern))
                    continue;

                string regexPattern = ConvertGitIgnorePatternToRegex(pattern);
                ignorePatterns.Add(new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }

            foreach (var pattern in additionalPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern) || pattern.StartsWith("#") || pattern.StartsWith("!"))
                    continue;

                var p = pattern.Trim();
                if (string.IsNullOrEmpty(p))
                    continue;

                string regexPattern = ConvertGitIgnorePatternToRegex(p);
                ignorePatterns.Add(new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }
        }

        private string ConvertGitIgnorePatternToRegex(string pattern)
        {
            string cleanedPattern = pattern.TrimEnd();
            bool isDirectoryOnly = cleanedPattern.EndsWith("/");
            if (isDirectoryOnly)
                cleanedPattern = cleanedPattern.Substring(0, cleanedPattern.Length - 1);

            string escaped = Regex.Escape(cleanedPattern);

            escaped = escaped
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*")
                .Replace("\\?", ".");

            string regexPattern;
            if (pattern.StartsWith("/"))
            {
                regexPattern = "^" + escaped;
            }
            else
            {
                regexPattern = "(^|/)" + escaped;
            }

            if (isDirectoryOnly)
            {
                regexPattern += "(?:/.*|$)";
            }

            return regexPattern;
        }

        public bool IsIgnored(string relativePath)
        {
            relativePath = relativePath.Replace("\\", "/");
            foreach (var pattern in ignorePatterns)
            {
                if (pattern.IsMatch(relativePath))
                    return true;
            }
            return false;
        }
    }
}
