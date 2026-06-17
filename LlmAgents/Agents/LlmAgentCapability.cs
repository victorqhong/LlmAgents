namespace LlmAgents.Agents;

public class AgentCapability
{
    protected readonly LlmAgent agent;

    public AgentCapability(LlmAgent agent)
    {
        this.agent = agent;
    }
}
