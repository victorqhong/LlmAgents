namespace LlmAgents.Agents;

public class LlmAgentParameters
{
    public required string AgentId;
    public required bool Persistent;
    public required string StorageDirectory;
    public required bool StreamOutput;
}