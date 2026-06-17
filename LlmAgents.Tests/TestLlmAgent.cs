using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using LlmAgents.Agents;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using LlmAgents.State;
using LlmAgents.Tests.Communication;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static LlmAgents.Agents.Capabilities.SessionCapability;

namespace LlmAgents.Tests;

[TestClass]
public sealed class TestLlmAgent 
{
    private class TestTool : Tool
    {
        public bool ToolCalled { get; set; }

        public TestTool(ToolFactory toolFactory) : base(toolFactory)
        {
        }

        public override ChatCompletionFunctionTool Schema { get; protected set; } = new()
        {
            Function = new()
            {
                Name = "shell",
                Description = $"Tool used for unit testing",
                Parameters = new()
                {
                    Properties = [],
                    Required = []
                }
            }
        };

        public override Task<JsonNode> Function(Session session, JsonDocument parameters)
        {
            ToolCalled = true;

            return Task.FromResult<JsonNode>(new JsonObject
            {
                { "success", "true" }
            });
        }
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        public TestHttpMessageHandler(params string[] responseStreams)
        {
            this.responseStreams = new Queue<string>(responseStreams);
        }

        private readonly Queue<string> responseStreams;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var responseStream = responseStreams.Dequeue();
            var stream = File.OpenRead(responseStream);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            };

            return Task.FromResult(response);
        }
    }

    private static LlmAgent CreateAgent(params string[] responseStreams)
    {
        var parameters = new LlmAgentParameters
        {
            StreamOutput = false,
            AgentId = "unit_test",
            Persistent = false,
            StorageDirectory = Environment.CurrentDirectory,
            AgentManagerUrl = null
        };

        var httpMessageHandler = new TestHttpMessageHandler(responseStreams);
        var httpClient = new HttpClient(httpMessageHandler);
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var llmApiParameters = new LlmApiConfig
        {
            ContextSize = 8192,
            MaxCompletionTokens = 8192,
            ApiModel = "unitetest",
            ApiKey = "sk-none",
            ApiEndpoint = "http://localhost",
        };
        var llmApi = new LlmApiOpenAi(loggerFactory, llmApiParameters, httpClient);
        return new LlmAgent(parameters, llmApi, loggerFactory);
    }

    [TestMethod]
    public async Task TestRun()
    {
        var agent = CreateAgent("Responses/response_stream.txt");
        var cts = new CancellationTokenSource();

        var messageCount = -1;
        agent.PostProcessSession += session =>
        {
            var messages = session.GetMessages();
            messageCount = messages.Count;
            cts.Cancel();
        };

        var communication = new UnitTestCommunication();
        communication.Input.Add("hi");

        var metadata = new SessionMetadata("unittest", "unittest", null);
        var handle = await agent.SessionCapability.CreateSession(metadata, communication, cts.Token);

        var messageContent = await communication.WaitForContent(cts.Token);
        await agent.PostInput(handle, messageContent!, cts.Token);

        cts.CancelAfter(10_000);

        Assert.AreEqual(0, communication.Output.Count);

        await agent.Run(cts.Token);

        Assert.AreEqual(2, messageCount);
        Assert.AreEqual(1, communication.Output.Count);
    }

    [TestMethod]
    public async Task TestToolCall()
    {
        var agent = CreateAgent("Responses/response_toolcall_stream.txt", "Responses/response_stream.txt");
        var cts = new CancellationTokenSource();

        var loggerFactory = new LoggerFactory();
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register<ILoggerFactory>(loggerFactory);

        var tool = new TestTool(toolFactory);
        agent.ToolCallCapability.AddToolDefinition(tool);

        int? messageCount = null;
        agent.PostProcessSession += session =>
        {
            var messages = session.GetMessages();
            messageCount = messages.Count;
            Console.WriteLine(JsonSerializer.Serialize(messages));
            cts.Cancel();
        };

        var communication = new UnitTestCommunication();
        var metadata = new SessionMetadata("unittest", "unittest", null);
        var handle = await agent.SessionCapability.CreateSession(metadata, communication, cts.Token);
        var session = agent.SessionCapability.GetSession(handle);

        await agent.ToolCallCapability.InitializeSessionTools(session);
        var tools = agent.ToolCallCapability.GetToolDefinitions(session);
        Assert.IsTrue(tools.Count > 0);

        communication.Input.Add("use the shell tool to determine the current date and time");
        var messageContent = await communication.WaitForContent(cts.Token);
        await agent.PostInput(handle, messageContent!, cts.Token);

        cts.CancelAfter(10_000);

        Assert.AreEqual(0, communication.Output.Count);

        await agent.Run(cts.Token);

        Assert.AreEqual(4, messageCount);
        Assert.AreEqual(1, communication.Output.Count);
        Assert.IsTrue(tool.ToolCalled);
    }
}
