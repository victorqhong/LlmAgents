using LlmAgents.LlmApi;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Simulation.Tests;

[TestClass]
public sealed class TestLlmApiOpenAi
{
    [TestMethod]
    public void TestGetPayload_Messages()
    {
        var model = "gpt-4o";
        var messages = new List<JObject>()
        {
            JObject.FromObject(new { role = "system", content = "this is the system prompt" }),
            JObject.FromObject(new { role = "user", content = "this is the user message" }),
            JObject.FromObject(new { role = "assistant", content = "this is the assistant message" })
        };
        var maxTokens = 100;
        var temperature = 0.7;

        var payload = LlmApiOpenAi.GetPayload(model, messages, maxTokens, temperature);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"system\",\"content\":\"this is the system prompt\"},{\"role\":\"user\",\"content\":\"this is the user message\"},{\"role\":\"assistant\",\"content\":\"this is the assistant message\"}],\"max_completion_tokens\":100,\"temperature\":0.7}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_NoMaxCompletionTokens()
    {
        var model = "gpt-4o";
        var messages = new List<JObject>();

        var payload = LlmApiOpenAi.GetPayload(model, messages, null, 1);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[],\"temperature\":1.0}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_NoTools()
    {
        var model = "gpt-4o";
        var messages = new List<JObject>();
        var maxTokens = 100;
        var temperature = 0.7;

        var payload = LlmApiOpenAi.GetPayload(model, messages, maxTokens, temperature);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[],\"max_completion_tokens\":100,\"temperature\":0.7}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_Tools()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register(loggerFactory);
        var shellTool = new Shell(toolFactory);

        var model = "gpt-4o";
        var messages = new List<JObject>();
        var maxTokens = 100;
        var temperature = 0.7;
        var tools = new List<JObject>() { shellTool.Schema };
        var toolChoice = "auto";

        var payload = JObject.Parse(LlmApiOpenAi.GetPayload(model, messages, maxTokens, temperature, tools, toolChoice));
        Assert.AreEqual("shell", payload["tools"]?[0]?["function"]?["name"]);
    }

    [TestMethod]
    public async Task TestProcessCompletion_Tools()
    {
        var json = System.IO.File.ReadAllText("Responses/response_toolcall.json");
        var completion = JObject.Parse(json);

        var loggerFactory = LoggerFactory.Create(builder => { });
        var agent = new LlmApiOpenAi(loggerFactory, "unittest", "http://localhost", "sk-none", "gpt-4o");
        await agent.ProcessCompletion(completion);
    }
}
