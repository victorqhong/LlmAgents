namespace LlmAgents.Configuration;

public class AgentConfig
{
    public required string ApiConfig { get; set; }
    public required string XmppConfig { get; set; }
    public required string ToolsConfig { get; set; }
    public required bool Persistent { get; set; }
    public required string WorkingDirectory { get; set; }
    public required string AgentDirectory { get; set; }
}
