namespace LlmAgents.Configuration;

public class ToolsConfig
{
    public required Dictionary<string, string> Assemblies { get; set; }
    public required List<string> Types { get; set; }
    public required Dictionary<string, string> Parameters { get; set; }
}

