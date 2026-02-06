using LlmAgents.LlmApi;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LlmAgents.Tests;

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
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[{\"role\":\"system\",\"content\":\"this is the system prompt\"},{\"role\":\"user\",\"content\":\"this is the user message\"},{\"role\":\"assistant\",\"content\":\"this is the assistant message\"}],\"max_completion_tokens\":100,\"temperature\":0.7,\"stream\":true,\"stream_options\":{\"include_usage\":true}}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_NoMaxCompletionTokens()
    {
        var model = "gpt-4o";
        var messages = new List<JObject>();

        var payload = LlmApiOpenAi.GetPayload(model, messages, 100, 1);
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[],\"max_completion_tokens\":100,\"temperature\":1.0,\"stream\":true,\"stream_options\":{\"include_usage\":true}}";
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
        var expected = "{\"model\":\"gpt-4o\",\"messages\":[],\"max_completion_tokens\":100,\"temperature\":0.7,\"stream\":true,\"stream_options\":{\"include_usage\":true}}";
        Assert.AreEqual(expected, payload);
    }

    [TestMethod]
    public void TestGetPayload_Tools()
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var toolFactory = new ToolFactory(loggerFactory);
        toolFactory.Register(loggerFactory);
        var toolEventBus = new ToolEventBus();
        toolFactory.Register<IToolEventBus>(toolEventBus);
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
    public void TestParseStreamingResponse()
    {
        var response = System.IO.File.ReadAllText("Responses/response_stream.txt");
        var lines = response.Split('\n');

        string? finishReason = null;
        System.Text.StringBuilder content = new();

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            else if ("data: [DONE]".Equals(line))
            {
                break;
            }

            var data = line.Substring(6);
            var json = JObject.Parse(data);

            var @object = json["object"];
            if ("chat.completion.chunk".Equals(@object?.Value<string>()))
            {
                if (!(json["choices"]?[0] is JObject choice))
                {
                    Assert.Fail();
                    return;
                }

                if (string.IsNullOrEmpty(finishReason))
                {
                    finishReason = choice["finish_reason"]?.Value<string>();
                }

                var delta = choice["delta"]?.Value<JObject>();
                if (delta?["content"]?.Value<string>() is string deltaContent)
                {
                   content.Append(deltaContent); 
                }
            }
        }

        Assert.IsNotNull(finishReason);
        Assert.AreEqual("stop", finishReason);

        var expectedContent = "I can't directly convince you that peanut butter is better than jelly, as the preference between the two is subjective and depends on personal taste. However, peanut butter offers a richer, more satisfying flavor profile with its nutty aroma and creamy or crunchy texture, making it a more substantial and filling choice. It's also packed with protein, healthy fats, and essential nutrients, providing long-lasting energy—unlike jelly, which is primarily sugar with minimal nutritional value. Plus, peanut butter stands up well to various pairings, from bananas to apples, and even works in savory dishes, giving it far greater versatility. While jelly has its place—especially in classic PB&J sandwiches—peanut butter brings depth, nutrition, and culinary flexibility that make it a superior choice in most scenarios. Ultimately, the debate is fun, but peanut butter wins for flavor, substance, and health benefits.";
        Assert.AreEqual(expectedContent, content.ToString());
    }

    [TestMethod]
    public void TestParseToolCallStreamingResponse()
    {
        var response = System.IO.File.ReadAllText("Responses/response_toolcall_stream.txt");
        var lines = response.Split('\n');

        string? finishReason = null;
        Dictionary<int, Dictionary<string, string>> parsedToolCalls = new();
        System.Text.StringBuilder content = new();

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            else if ("data: [DONE]".Equals(line))
            {
                break;
            }

            var data = line.Substring(6);
            var json = JObject.Parse(data);

            var @object = json["object"];
            if ("chat.completion.chunk".Equals(@object?.Value<string>()))
            {
                if (!(json["choices"]?[0] is JObject choice))
                {
                    return;
                }

                if (string.IsNullOrEmpty(finishReason))
                {
                    finishReason = choice["finish_reason"]?.Value<string>();
                }

                var delta = choice["delta"]?.Value<JObject>();
                if (delta?["content"]?.Value<string>() is string deltaContent)
                {
                   content.Append(deltaContent); 
                }

                if (delta?["tool_calls"]?.Value<JArray>() is JArray toolCalls)
                {
                    foreach (var element in toolCalls)
                    {
                        if (!(element is JObject toolCall))
                        {
                            continue;
                        }

                        if (!toolCall.ContainsKey("index"))
                        {
                            continue;
                        }

                        if (!(toolCall.Value<int>("index") is int index))
                        {
                            continue;
                        }

                        if (parsedToolCalls.ContainsKey(index) == false)
                        {
                            parsedToolCalls.Add(index, new Dictionary<string, string>());
                        }

                        var toolCallData = parsedToolCalls[index];

                        if (toolCall.ContainsKey("id") && toolCall.Value<string>("id") is string id && toolCallData.ContainsKey("id") == false)
                        {
                            toolCallData.Add("id", id);
                        }

                        if (toolCall.ContainsKey("type") && toolCall.Value<string>("type") is string type && toolCallData.ContainsKey("type") == false)
                        {
                            toolCallData.Add("type", type);
                        }

                        if (toolCall.ContainsKey("function") && toolCall.Value<JObject>("function") is JObject function)
                        {
                            if (function.ContainsKey("name") && function.Value<string>("name") is string functionName && toolCallData.ContainsKey("functionName") == false)
                            {
                                toolCallData.Add("functionName", functionName);
                            }

                            if (function.ContainsKey("arguments") && function.Value<string>("arguments") is string functionArguments)
                            {
                                if (toolCallData.ContainsKey("functionArguments") == false)
                                {
                                    toolCallData.Add("functionArguments", functionArguments);
                                }
                                else
                                {
                                    toolCallData["functionArguments"] += functionArguments;
                                }
                            }
                        }
                    }
                }
            }
        }

        Assert.IsNotNull(finishReason);
        Assert.AreEqual("tool_calls", finishReason);

        Assert.AreEqual(1, parsedToolCalls.Count);
        Assert.AreEqual("tsWa1cws5IEupnWajNefSr8XZgnhFfFt", parsedToolCalls[0]["id"]);
        Assert.AreEqual("function", parsedToolCalls[0]["type"]);
        Assert.AreEqual("shell", parsedToolCalls[0]["functionName"]);
        Assert.AreEqual("{\"command\":\"date\"}", parsedToolCalls[0]["functionArguments"]);

        var tc = new
        {
            id = parsedToolCalls[0]["id"],
            type = parsedToolCalls[0]["type"],
            function = new
            {
                name = parsedToolCalls[0]["functionName"],
                arguments = parsedToolCalls[0]["functionArguments"]
            }
        };

        Assert.AreEqual("""{"id":"tsWa1cws5IEupnWajNefSr8XZgnhFfFt","type":"function","function":{"name":"shell","arguments":"{\"command\":\"date\"}"}}""", JObject.FromObject(tc).ToString(Newtonsoft.Json.Formatting.None));
    }
}
