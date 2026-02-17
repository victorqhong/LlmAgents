using GuiAgent.Agents.Work;
using LlmAgents.Agents;
using LlmAgents.Agents.Work;
using LlmAgents.Communication;
using LlmAgents.LlmApi;
using ModelContextProtocol.Client;

namespace GuiAgent.Agents;

internal class GuiAgent : LlmAgent
{
    protected McpClient? mcpClient;

    public GuiAgent(LlmAgentParameters parameters, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication) : base(parameters, llmApi, agentCommunication)
    {
    }

    public GuiAgent(string id, LlmApiOpenAi llmApi, IAgentCommunication agentCommunication) : base(id, llmApi, agentCommunication)
    {
    }

    protected override LlmAgentWork CreateUserInputWork()
    {
        return new GetUserInputWorkAndScreenshot(Clients.First(), this);
    }

    protected override async Task Turn(CancellationToken cancellationToken)
    {
        var screenshotWork = await RunWork(new GetScreenshot(Clients.First(), this), null, cancellationToken);
        var userInputWork = await RunWork(new GetUserInputWork(this), screenshotWork, cancellationToken);
        var assistantWork = await RunWork(new GetAssistantResponseWork(this), userInputWork, cancellationToken);

        while (assistantWork.Parser != null && string.Equals(assistantWork.Parser.FinishReason, "tool_calls"))
        {
            var toolCallsWork = await RunWork(new ToolCalls(this), assistantWork, cancellationToken);
            screenshotWork = await RunWork(new GetScreenshot(Clients.First(), this), toolCallsWork, cancellationToken);
            assistantWork = await RunWork(new GetAssistantResponseWork(this), screenshotWork, cancellationToken);
        }
    }
}
