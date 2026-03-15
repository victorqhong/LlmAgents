using System;
using System.Collections.Generic;
using System.Text.Json;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlmAgents.Tests;

[TestClass]
public class TestSerialization
{
    [TestMethod]
    public void TestChatCompletionRequest_ContentString()
    {
        var model = "gpt-4o";
        var messages = new List<ChatCompletionMessageParam>()
        {
            new() { Role = "system", Content = new ChatCompletionMessageParamContentString { Content = "this is the system prompt" } },
            new() { Role = "user", Content = new ChatCompletionMessageParamContentString { Content = "this is the user message" } },
            new() { Role = "assistant", Content = new ChatCompletionMessageParamContentString { Content = "this is the assistant message" } }
        };
        var maxTokens = 100;
        var temperature = 0.7;

        var chatCompletionRequest = new ChatCompletionRequest(true)
        {
            Model = model,
            Messages = messages,
            Temperature = temperature,
            MaxCompletionTokens = maxTokens,
        };

        var json = JsonSerializer.Serialize(chatCompletionRequest);
        Assert.IsFalse(string.IsNullOrEmpty(json));
        Console.WriteLine(json);

        var deserialized = JsonSerializer.Deserialize<ChatCompletionRequest>(json);
        Assert.IsNotNull(deserialized);
        Assert.IsTrue(deserialized.Messages.Count == 3);
        Assert.IsInstanceOfType<ChatCompletionMessageParamContentString>(deserialized.Messages[1].Content);
    }

    [TestMethod]
    public void TestChatCompletionRequest_ContentParts()
    {
        var model = "gpt-4o";
        var messages = new List<ChatCompletionMessageParam>()
        {
            new() { Role = "system", Content = new ChatCompletionMessageParamContentString { Content = "this is the system prompt" } },
            new() { Role = "user", Content = new ChatCompletionMessageParamContentParts { Content = [new ChatCompletionContentPartText { Text = "this is the user message" }]}},
            new() { Role = "assistant", Content = new ChatCompletionMessageParamContentString { Content = "this is the assistant message" } }
        };
        var maxTokens = 100;
        var temperature = 0.7;

        var chatCompletionRequest = new ChatCompletionRequest(true)
        {
            Model = model,
            MaxCompletionTokens = maxTokens,
            Temperature = temperature,
            Messages = messages
        };

        var json = JsonSerializer.Serialize(chatCompletionRequest);
        Assert.IsFalse(string.IsNullOrEmpty(json));

        var deserialized = JsonSerializer.Deserialize<ChatCompletionRequest>(json);
        Assert.IsNotNull(deserialized);
        Assert.IsTrue(deserialized.Messages.Count == 3);
        Assert.IsInstanceOfType<ChatCompletionMessageParamContentParts>(deserialized.Messages[1].Content);
    }

    [TestMethod]
    public void TestChatCompletionRequest_NoMaxCompletionTokens()
    {
        var model = "gpt-4o";
        var messages = new List<ChatCompletionMessageParam>();

        var chatCompletionRequest = new ChatCompletionRequest(true)
        {
            Model = model,
            Messages = messages,
            Temperature = 1.0
        };

        var json = JsonSerializer.Serialize(chatCompletionRequest);
        Assert.IsFalse(string.IsNullOrEmpty(json));

        var deserialized = JsonSerializer.Deserialize<ChatCompletionRequest>(json);
        Assert.IsNotNull(deserialized);
        Assert.IsNull(deserialized.MaxCompletionTokens);
    }

    [TestMethod]
    public void TestUsage_Deserialize()
    {
        var json = """{ "completion_tokens":1, "prompt_tokens":2, "total_tokens": 3}""";
        var usage = JsonSerializer.Deserialize<ChatCompletionUsage>(json);
        Assert.IsNotNull(usage);
        Assert.AreEqual(1, usage.CompletionTokens);
        Assert.AreEqual(2, usage.PromptTokens);
        Assert.AreEqual(3, usage.TotalTokens);

        json = """{"choices":[],"created":1773351153,"id":"chatcmpl-d9MIB8aGJ1qBwmjp8DBxubZU8fFPEWBk","model":"qwen3.5","system_fingerprint":"b8156-3769fe6eb","object":"chat.completion.chunk","usage":{"completion_tokens":49,"prompt_tokens":1133,"total_tokens":1182},"timings":{"cache_n":1054,"prompt_n":79,"prompt_ms":2483.286,"prompt_per_token_ms":31.434,"prompt_per_second":31.81268689953553,"predicted_n":49,"predicted_ms":2418.602,"predicted_per_token_ms":49.35922448979591,"predicted_per_second":20.259637592295054}}""";
        var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(json);
        Assert.IsNotNull(chunk);
        Assert.IsNotNull(chunk.Usage);
        Assert.AreEqual(49, chunk.Usage.CompletionTokens);
    }

    [TestMethod]
    public void TestChunk_Deserialize()
    {
        var finishReason = "\"tool_calls\"";
        var reason = JsonSerializer.Deserialize<ChatCompletionChoiceFinishReason>(finishReason);
        Assert.AreEqual(ChatCompletionChoiceFinishReason.ToolCalls, reason);

        var json = """{"choices":[{"finish_reason":"tool_calls","index":0,"delta":{}}],"created":1773348945,"id":"chatcmpl-CcsMIMPqkac4BT2WXkVuus9DCV2QHObJ","model":"qwen3.5","system_fingerprint":"b8156-3769fe6eb","object":"chat.completion.chunk"}""";
        var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(json);
        Assert.IsNotNull(chunk);
        Assert.IsTrue(chunk.Choices.Count > 0);
        Assert.AreEqual(ChatCompletionChoiceFinishReason.ToolCalls, chunk.Choices[0].FinishReason);
    }

    [TestMethod]
    public void TestTools()
    {
        var tool = new ChatCompletionFunctionTool
        {
            Function = new ChatCompletionFunctionDefinition
            {
                Name = "shell",
                Description = "Runs a shell command in bash. Prefer the 'file_write' tool for writing files.",
                Parameters = new ChatCompletionFunctionParameters
                {
                    Properties = new Dictionary<string, ChatCompletionFunctionParameter>
                    {
                        { "command", new ChatCompletionFunctionParameter { Type = "string", Description = "Shell command and arguments to run" } }
                    },
                    Required = ["command"]
                }
            }
        };

        var model = "gpt-4o";
        var maxTokens = 100;
        var temperature = 0.7;

        var request = new ChatCompletionRequest(true)
        {
            Model = model,
            MaxCompletionTokens = maxTokens,
            Temperature = temperature,
            Messages = [],
            ToolChoice = "auto",
            Tools = [tool]
        };

        var json = JsonSerializer.Serialize(request);
        Assert.IsFalse(string.IsNullOrEmpty(json));

        var deserialized = JsonSerializer.Deserialize<ChatCompletionRequest>(json);
        Assert.IsNotNull(deserialized);
        Assert.IsNotNull(deserialized.Tools);
        Assert.IsTrue(deserialized.Tools.Count > 0);
        Assert.AreEqual("shell", deserialized.Tools[0].Function.Name);
    }
}
