using GuiAgent.Agents.Work;
using LlmAgents.Agents;
using LlmAgents.Communication;
using LlmAgents.LlmApi;

namespace GuiAgent.Agents;

internal class GuiAgent : LlmAgent
{
    public GuiAgent(LlmAgentParameters parameters, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication) : base(parameters, llmApi, agentCommunication)
    {
    }

    public GuiAgent(string id, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication) : base(id, llmApi, agentCommunication)
    {
    }

    protected override LlmAgentWork CreateUserInputWork()
    {
        return new GetUserInputWorkAndScreenshot(this);
    }
}
