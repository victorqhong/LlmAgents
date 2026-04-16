namespace LlmAgents.Skills;

public class Skill
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public List<string> Tags { get; set; } = [];
    public List<string> Triggers { get; set; } = [];
    public string FilePath { get; set; } = string.Empty;
    public string FullContent { get; set; } = string.Empty;
    public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
}
