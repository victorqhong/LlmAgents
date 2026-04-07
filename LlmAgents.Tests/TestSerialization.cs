using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlmAgents.LlmApi.Llamacpp;
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
            new ChatCompletionMessageParamSystem() { Content = new ChatCompletionMessageParamContentString { Content = "this is the system prompt" } },
            new ChatCompletionMessageParamUser() { Content = new ChatCompletionMessageParamContentString { Content = "this is the user message" } },
            new ChatCompletionMessageParamAssistant() { Content = new ChatCompletionMessageParamContentString { Content = "this is the assistant message" } }
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
            new ChatCompletionMessageParamSystem() { Content = new ChatCompletionMessageParamContentString { Content = "this is the system prompt" } },
            new ChatCompletionMessageParamUser() { Content = new ChatCompletionMessageParamContentParts { Content = [new ChatCompletionContentPartText { Text = "this is the user message" }]}},
            new ChatCompletionMessageParamAssistant() { Content = new ChatCompletionMessageParamContentString { Content = "this is the assistant message" } }
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

    [TestMethod]
    public void TestMessageParam_ContentPartsText()
    {
        var json = """[{"role":"user","content":[{"text":"hi","type":"text"}]},{"role":"assistant","content":"Hello! How can I assist you today? I\u0027m here to help with any questions or tasks you have. Whether you need information, want to work with files, run commands, or anything else, just let me know what you need!","reasoning_content":"Hello! How can I assist you today? I\u0027m ready to help with any questions or tasks you have. Whether you need information, want to work with files, run commands, or anything else, just let me know what you need!\n"}]""";

        List<ChatCompletionMessageParam>? messages = JsonSerializer.Deserialize<List<ChatCompletionMessageParam>>(json);

        Assert.IsNotNull(messages);
        Assert.AreEqual(2, messages.Count);
        Assert.IsInstanceOfType<ChatCompletionMessageParamUser>(messages[0]);
        Assert.IsInstanceOfType<ChatCompletionMessageParamContentParts>(messages[0].Content);

        var contentParts = messages[0].Content as ChatCompletionMessageParamContentParts;
        Assert.IsNotNull(contentParts);

        Assert.AreEqual(1, contentParts.Content.Count);

        var part = contentParts.Content[0];
        Assert.IsInstanceOfType<ChatCompletionContentPartText>(part);

        var textPart = part as ChatCompletionContentPartText;
        Assert.IsNotNull(textPart);
        Assert.AreEqual("text", textPart.Type);
        Assert.AreEqual("hi", textPart.Text);
    }

    [TestMethod]
    public void TestMessageParam_ToolCall()
    {
        var messages = new List<ChatCompletionMessageParam>();

        var systemMessage = new ChatCompletionMessageParamSystem
        {
            Content = new ChatCompletionMessageParamContentString { Content = "You are a helpful assistant" }
        };
        messages.Add(systemMessage);

        var userMessage = new ChatCompletionMessageParamUser
        {
            Content = new ChatCompletionMessageParamContentString { Content = "What's the weather in Paris?" }
        };
        messages.Add(userMessage);

        var assistantMessage = new ChatCompletionMessageParamAssistant
        {
            Content = null,
            ToolCalls = [
                new ChatCompletionMessageFunctionToolCall
                {
                    Id = "abc123",
                    Type = "function",
                    Function = new ChatCompletionMessageFunctionToolCallFunction
                    {
                        Name = "get_weather",
                        Arguments = "{\"location\":\"Paris\"}"
                    }
                }

            ]
        };
        messages.Add(assistantMessage);

        var toolMessage = new ChatCompletionMessageParamTool
        {
            ToolCallId = "abc123",
            Content = new ChatCompletionMessageParamContentString { Content = "{\"temperature\":80}" },
            Name = "get_weather"
        };
        messages.Add(toolMessage);

        var json = JsonSerializer.Serialize(messages);

        messages.Clear();
        Assert.AreEqual(0, messages.Count);

        messages = JsonSerializer.Deserialize<List<ChatCompletionMessageParam>>(json);
        Assert.IsNotNull(messages);
        Assert.AreEqual(4, messages.Count);
        Assert.IsInstanceOfType<ChatCompletionMessageParamAssistant>(messages[2]);

        var message = messages[2] as ChatCompletionMessageParamAssistant;
        Assert.IsNotNull(message);
        Assert.IsNotNull(message.ToolCalls);
        Assert.AreEqual(1, message.ToolCalls.Count);
        Assert.AreEqual("abc123", message.ToolCalls[0].Id);
        Assert.AreEqual("get_weather", message.ToolCalls[0].Function.Name);
        Assert.AreEqual("{\"location\":\"Paris\"}", message.ToolCalls[0].Function.Arguments);

    }

    [TestMethod]
    public void LlamacppChatCompletionRequest_Serialization()
    {
        var messages = new List<ChatCompletionMessageParam>()
        {
            new ChatCompletionMessageParamSystem() { Content = new ChatCompletionMessageParamContentString { Content = "this is the system prompt" } },
            new ChatCompletionMessageParamUser() { Content = new ChatCompletionMessageParamContentString { Content = "this is the user message" } },
            new ChatCompletionMessageParamAssistant() { Content = new ChatCompletionMessageParamContentString { Content = "this is the assistant message" } }
        };

        LlamacppChatCompletionRequest request = new()
        {
            Model = "test",
            Messages = messages,
            SlotId = 0
        };

        var json = JsonSerializer.Serialize(request);
        Assert.IsTrue(json.Contains("id_slot"));

        var deserialized = JsonSerializer.Deserialize<LlamacppChatCompletionRequest>(json);
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(0, deserialized.SlotId);
        Assert.AreEqual(3, deserialized.Messages.Count);
        Assert.AreEqual("test", deserialized.Model);
    }

    [TestMethod]
    public void McpTool()
    {
        var json = """{"type":"object","properties":{"operations":{"type":"array","items":{"type":"object","properties":{"operation":{"type":"string","enum":["add","edit"]},"entityType":{"type":"string","enum":["block","page","tag","property"]},"id":{"type":["string","number","null"]},"data":{"type":"object","properties":{},"additionalProperties":true}},"required":["operation","entityType","data"],"additionalProperties":false}},"dry-run":{"type":"boolean","description":"Pretend to do batch update. Does everything except actually commit change to db e.g. validation."}},"required":["operations"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}""";
        var doc = JsonDocument.Parse(json);
        var el = doc.RootElement;
        var functionParameters = el.Deserialize<ChatCompletionFunctionParameters>();
        Assert.IsNotNull(functionParameters);
    }
}
