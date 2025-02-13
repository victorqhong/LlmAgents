using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using Simulation;
using System;
using System.Collections.Generic;

[TestClass]
public sealed class TestLlmAgentApi
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

        var payload = LlmAgentApi.GetPayload(model, messages, maxTokens, temperature);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"system\",\"content\":\"this is the system prompt\"},{\"role\":\"user\",\"content\":\"this is the user message\"},{\"role\":\"assistant\",\"content\":\"this is the assistant message\"}],\"max_tokens\":100,\"temperature\":0.7}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_NoMaxTokens()
    {
        var model = "gpt-4o";
        var messages = new List<JObject>();

        var payload = LlmAgentApi.GetPayload(model, messages);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[],\"temperature\":0.7}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_NoTools()
    {
        var model = "gpt-4o";
        var messages = new List<JObject>();
        var maxTokens = 100;
        var temperature = 0.7;

        var payload = LlmAgentApi.GetPayload(model, messages, maxTokens, temperature);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[],\"max_tokens\":100,\"temperature\":0.7}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_Tools()
    {
        var model = "gpt-4o";
        var messages = new List<JObject>();
        var maxTokens = 100;
        var temperature = 0.7;
        var tools = new List<JObject>() { JObject.FromObject(Simulation.Tools.Shell.Definition) };
        var toolChoice = "auto";

        var payload = LlmAgentApi.GetPayload(model, messages, maxTokens, temperature, tools, toolChoice);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[],\"max_tokens\":100,\"temperature\":0.7,\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"shell\",\"description\":\"Runs a shell command\",\"parameters\":{\"type\":\"object\",\"properties\":{\"command\":{\"type\":\"string\",\"description\":\"Shell command and arguments to run\"}},\"required\":[\"command\"]}}}],\"tool_choice\":\"auto\"}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestProcessCompletion_Tools()
    {
        var json = System.IO.File.ReadAllText("Responses/response_toolcall.json");
        var completion = JObject.Parse(json);

        var agent = new LlmAgentApi("http://localhost", "sk-none", "gpt-4o");
        agent.ProcessCompletion(completion);
    }
}
