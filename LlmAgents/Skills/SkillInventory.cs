using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LlmAgents.Skills;

public class SkillInventory
{
    private readonly ILogger log;
    private readonly Dictionary<string, Skill> skillsByName = [];
    private readonly Dictionary<string, List<Skill>> skillsByTag = [];
    private string? skillsDirectory;

    public SkillInventory(ILoggerFactory loggerFactory)
    {
        log = loggerFactory.CreateLogger<SkillInventory>();
    }

    public void SetSkillsDirectory(string directory)
    {
        skillsDirectory = directory;
    }

    public void Load()
    {
        if (string.IsNullOrEmpty(skillsDirectory) || !Directory.Exists(skillsDirectory))
        {
            return;
        }

        // Clear existing skills
        skillsByName.Clear();
        skillsByTag.Clear();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        foreach (var file in Directory.GetFiles(skillsDirectory, "*.md"))
        {
            try
            {
                var skill = ParseSkillFile(file, deserializer);
                if (skill != null)
                {
                    skillsByName[skill.Name] = skill;

                    foreach (var tag in skill.Tags)
                    {
                        if (!skillsByTag.TryGetValue(tag, out List<Skill>? value))
                        {
                            value = [];
                            skillsByTag[tag] = value;
                        }

                        value.Add(skill);
                    }
                }
            }
            catch (Exception e)
            {
                log.LogError(e, "Failed to load skill from: {file}", file);
            }
        }
    }

    public void Refresh()
    {
        Load();
    }

    private Skill? ParseSkillFile(string filePath, IDeserializer deserializer)
    {
        var content = File.ReadAllText(filePath);

        // Parse YAML frontmatter (between --- markers)
        var frontmatterMatch = Regex.Match(content, @"^---\s*\n(.*?)\n---", RegexOptions.Singleline);
        if (!frontmatterMatch.Success)
        {
            return null;
        }

        var yaml = frontmatterMatch.Groups[1].Value;
        var metadata = deserializer.Deserialize<SkillMetadata>(yaml);

        if (string.IsNullOrEmpty(metadata?.Name))
        {
            return null;
        }

        return new Skill
        {
            Name = metadata.Name,
            Description = metadata.Description ?? string.Empty,
            Version = metadata.Version ?? "1.0",
            Tags = metadata.Tags ?? [],
            Triggers = metadata.Triggers ?? [],
            FilePath = filePath,
            FullContent = content
        };
    }

    public Skill? GetSkill(string name) => skillsByName.GetValueOrDefault(name);

    public IEnumerable<Skill> GetAllSkills() => skillsByName.Values;

    public IEnumerable<Skill> GetSkillsByTag(string tag) =>
        skillsByTag.GetValueOrDefault(tag, []);

    public int Count => skillsByName.Count;

    public IReadOnlyDictionary<string, Skill> AllSkills => skillsByName;
}

internal class SkillMetadata
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? Triggers { get; set; }
}
