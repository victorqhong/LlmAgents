namespace LlmAgents.Skills;

public class SkillCatalogGenerator
{
    public string Generate(SkillInventory inventory)
    {
        var skills = inventory.GetAllSkills().ToList();

        if (skills.Count == 0)
        {
            return @"
## Available Skills
No skills are currently available.
";
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(@"
## Available Skills

The following specialized skills are available. When a user request matches a skill's triggers or tags, use the 'file_read' tool to read the skill file and follow its structured workflow.");

        foreach (var skill in skills.OrderBy(s => s.Name))
        {
            sb.AppendLine();
            sb.AppendLine($"### {skill.Name}");
            sb.AppendLine($"**File:** `{skill.FilePath}`");
            sb.AppendLine($"**Description:** {skill.Description}");

            if (skill.Tags.Count > 0)
            {
                sb.AppendLine($"**Tags:** {string.Join(", ", skill.Tags.Select(t => $"`{t}`"))}");
            }

            if (skill.Triggers.Count > 0)
            {
                sb.AppendLine($"**Triggers:** {string.Join(", ", skill.Triggers.Select(t => $"\"{t}\""))}");
            }

            sb.AppendLine();
            sb.AppendLine("---");
        }

        sb.AppendLine();
        sb.AppendLine(@"
### How to Use Skills
1. When a user request matches a skill's triggers, read the skill file using `file_read`
2. Follow the workflow defined in the skill
3. If no skill matches, proceed with general best practices
");

        return sb.ToString();
    }

    public string GenerateCompact(SkillInventory inventory)
    {
        var skills = inventory.GetAllSkills().ToList();

        if (skills.Count == 0)
        {
            return "Skills: (none available)";
        }

        return $"Skills: {string.Join(" | ", skills.Select(s =>
            $"{s.Name}[{string.Join(",", s.Tags)}]"))}";
    }
}
