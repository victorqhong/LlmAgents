namespace LlmAgents.Tools;

using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public class FileList : Tool
{
    private readonly string basePath;
    private readonly bool restrictToBasePath;

    public FileList(ToolFactory toolFactory)
        : base(toolFactory)
    {
        basePath = Path.GetFullPath(toolFactory.GetParameter(nameof(basePath)) ?? Environment.CurrentDirectory);
        restrictToBasePath = bool.TryParse(toolFactory.GetParameter(nameof(restrictToBasePath)), out restrictToBasePath) ? restrictToBasePath : true;
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
                        description = "Whether to recursively list files in all subdirectories"
                    },
                    searchPattern = new
                    {
                        type = "string",
                        description = "The search string to match against the names of files in path (default *.*). This parameter can contain a combination of valid literal path and wildcard (* and ?) characters, but it doesn't support regular expressions."
                    },
                    useGitIgnore = new
                    {
                        type = "boolean",
                        description = "Whether to use a .gitignore file to filter results (default is true)"
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
        var useGitIgnore = parameters["useGitIgnore"]?.Value<bool>() ?? true;

        try
        {
            if (restrictToBasePath && !Path.IsPathRooted(path))
            {
                path = Path.Combine(basePath, path);
            }

            path = Path.GetFullPath(path);

            if (restrictToBasePath && !path.StartsWith(basePath))
            {
                result.Add("error", $"cannot list files outside {basePath}");
                return Task.FromResult<JToken>(result);
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(path, searchPattern, searchOption);
            var directories = Directory.GetDirectories(path, searchPattern, searchOption);

            var listResults = directories.Concat(files);

            if (useGitIgnore)
            {
                var gitIgnoreFile = Path.Combine(path, ".gitignore");
                if (File.Exists(gitIgnoreFile))
                {
                    var filter = new GitIgnoreFilter(gitIgnoreFile, ".git/");
                    listResults = filter.FilterPaths(listResults);
                }
            }

            return Task.FromResult<JToken>(JArray.FromObject(listResults));
        }
        catch (Exception e)
        {
            result.Add("exception", e.Message);
        }

        return Task.FromResult<JToken>(result);
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
            // Normalize pattern: remove trailing spaces, handle directory slashes
            string cleanedPattern = pattern.TrimEnd();
            bool isDirectoryOnly = cleanedPattern.EndsWith("/");
            if (isDirectoryOnly)
                cleanedPattern = cleanedPattern.Substring(0, cleanedPattern.Length - 1);

            // Escape special regex characters
            string escaped = Regex.Escape(cleanedPattern);

            // Replace .gitignore wildcards with regex equivalents
            escaped = escaped
                .Replace("\\*\\*", ".*")        // ** matches any sequence, including /
                .Replace("\\*", "[^/]*")       // * matches any sequence except /
                .Replace("\\?", ".");          // ? matches any single character

            // Handle anchoring:
            // - If pattern starts with '/', anchor to root
            // - Otherwise, allow match anywhere in path (but still respect directory boundaries)
            string regexPattern;
            if (pattern.StartsWith("/"))
            {
                regexPattern = "^" + escaped;
            }
            else
            {
                // Allow match at start or after a slash
                regexPattern = "(^|/)" + escaped;
            }

            // If directory-only, ensure it matches a slash and anything after
            if (isDirectoryOnly)
            {
                regexPattern += "(?:/.*|$)";
            }

            return regexPattern;
        }

        public List<string> FilterPaths(IEnumerable<string> paths)
        {
            return paths.Where(path => !IsIgnored(path)).ToList();
        }

        private bool IsIgnored(string path)
        {
            path = path.Replace("\\", "/");
            foreach (var pattern in ignorePatterns)
            {
                if (pattern.IsMatch(path))
                    return true;
            }
            return false;
        }
    }

}

