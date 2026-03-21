using System;
using System.Threading;
using System.Threading.Tasks;
using LlmAgents.Agents;
using LlmAgents.Agents.Work;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.Tests.Communication;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests;

[TestClass]
public sealed class TestLlmAgent 
{
    private static LlmAgent CreateAgent(out UnitTestCommunication communication)
    {
        var parameters = new LlmAgentParameters
        {
            StreamOutput = false,
            AgentId = "unit_test",
            Persistent = false,
            StorageDirectory = Environment.CurrentDirectory,
            AgentManagerUrl = null
        };

        var loggerFactory = new LoggerFactory();
        var llmApiParameters = new LlmApiConfig
        {
            ContextSize = 8192,
            MaxCompletionTokens = 8192,
            ApiModel = Environment.GetEnvironmentVariable("LLMAGENTS_API_MODEL")!,
            ApiKey = Environment.GetEnvironmentVariable("LLMAGENTS_API_KEY")!,
            ApiEndpoint = Environment.GetEnvironmentVariable("LLMAGENTS_API_ENDPOINT")!,
        };
        var llmApi = new LlmApiOpenAi(loggerFactory, llmApiParameters);
        communication = new UnitTestCommunication();
        return new LlmAgent(parameters, llmApi, communication, loggerFactory);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Integration)]
    public async Task TestAgent_RunWork()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(15_000);

        var loggerFactory = new LoggerFactory();
        var agent = CreateAgent(out var communication);

        var messages = agent.RenderConversation();
        Assert.AreEqual(0, messages.Count);

        communication.Input.Add("hi");
        var userInputWork = await agent.RunWork(new GetUserInputWork(agent), null, cts.Token);
        messages = agent.RenderConversation();
        Assert.AreEqual(1, messages.Count);
        Assert.IsInstanceOfType<ChatCompletionMessageParamUser>(messages[0]);

        await agent.RunWork(new GetAssistantResponseWork(loggerFactory, agent), userInputWork, cts.Token);
        messages = agent.RenderConversation();
        Assert.AreEqual(2, messages.Count);
        Assert.IsInstanceOfType<ChatCompletionMessageParamAssistant>(messages[1]);
    }

    [TestMethod]
    [TestCategory(Constants.TestCategory_Integration)]
    public async Task TestAgent_ToolCall()
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(15_000);

        var loggerFactory = new LoggerFactory();
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);
        var agent = CreateAgent(out var communication);
        agent.AddTool(new Shell(toolFactory));

        var tools = agent.GetToolDefinitions();
        Assert.IsTrue(tools.Count > 0);

        var messages = agent.RenderConversation();
        Assert.AreEqual(0, messages.Count);

        communication.Input.Add("use the shell tool to determine the current date and time");
        var userInputWork = await agent.RunWork(new GetUserInputWork(agent), null, cts.Token);
        messages = agent.RenderConversation();
        Assert.AreEqual(1, messages.Count);

        var assistantResponse = await agent.RunWork(new GetAssistantResponseWork(loggerFactory, agent), userInputWork, cts.Token);
        messages = agent.RenderConversation();
        Assert.AreEqual(2, messages.Count);
        Assert.IsNotNull(assistantResponse.Parser);
        Assert.AreEqual(ChatCompletionChoiceFinishReason.ToolCalls, assistantResponse.Parser.FinishReason);
    }
}
