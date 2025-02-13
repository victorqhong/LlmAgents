namespace Simulation;

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

public class LlmAgentApi
{
    public static List<Type> Tools = new List<Type>()
    {
        typeof(Simulation.Tools.Shell)
    };

    public static List<JObject> ToolDefinitions = new List<JObject>();
    public static Dictionary<string, Func<JObject, JObject>> ToolMap = new Dictionary<string, Func<JObject, JObject>>();

    static LlmAgentApi()
    {
        foreach (var tool in Tools)
        {
            var definitionField = tool.GetField("Definition", BindingFlags.Public | BindingFlags.Static);
            var definition = definitionField?.GetValue(null) as JObject;
            if (definition == null)
            {
                continue;
            }

            ToolDefinitions.Add(definition);

            var name = definition["function"]?["name"]?.Value<string>();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var functionField = tool.GetField("Function", BindingFlags.Public | BindingFlags.Static);
            var function = functionField?.GetValue(null) as Func<JObject, JObject>;
            if (function == null)
            {
                continue;
            }

            ToolMap.Add(name, function);
        }
    }

    public LlmAgentApi(string apiEndpoint, string apiKey, string model, string? systemPrompt = null)
    {
        ApiEndpoint = apiEndpoint;
        ApiKey = apiKey;
        Model = model;

        if (systemPrompt != null)
        {
            Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));
        }
    }

    public List<JObject> Messages { get; private set; } = new List<JObject>();

    public string ApiEndpoint { get; private set; }

    public string ApiKey { get; private set; }

    public string Model { get; set; }

    public int MaxTokens { get; set; } = 100;

    public double Temperature { get; set; } = 0.7;

    public string GenerateCompletion(string userMessage)
    {
        Messages.Add(JObject.FromObject(new { role = "user", content = userMessage }));
        return GenerateCompletion(Messages);
    }

    public string GenerateCompletion(List<JObject> messages)
    {
        var payload = GetPayload(Model, messages, MaxTokens, Temperature, ToolDefinitions, "auto");

        var completion = Post(ApiEndpoint, ApiKey, payload).ConfigureAwait(false).GetAwaiter().GetResult();
        if (completion == null)
        {
            return string.Empty;
        }

        return ProcessCompletion(completion) ?? string.Empty;
    }

    public string? ProcessCompletion(JObject completion)
    {
        var choice = completion["choices"]?[0];
        if (choice == null)
        {
            return null;
        }

        var message = choice["message"];
        if (message == null)
        {
            return null;
        }

        var finishReason = choice["finish_reason"]?.ToString();
        if (finishReason == null)
        {
            return null;
        }

        var content = message["content"]?.ToString();
        if (string.Equals(finishReason, "stop"))
        {
            if (!string.IsNullOrEmpty(content))
            {
                Messages.Add(JObject.FromObject(new { role = "assistant", content }));
            }

            return content;
        }
        else if (string.Equals(finishReason, "tool_calls"))
        {
            var toolCalls = message["tool_calls"];
            if (toolCalls == null)
            {
                return null;
            }

            Messages.Add(JObject.FromObject(new { role = "assistant", content, tool_calls = toolCalls }));

            foreach (var toolCall in toolCalls)
            {
                var function = toolCall["function"];
                if (function == null)
                {
                    return null;
                }

                var name = function["name"]?.Value<string>();
                if (string.IsNullOrEmpty(name))
                {
                    return null;
                }

                if (!ToolMap.ContainsKey(name))
                {
                    return null;
                }

                var tool = ToolMap[name];

                var arguments = function["arguments"]?.Value<string>();
                if (arguments == null)
                {
                    return null;
                }

                var id = toolCall["id"]?.Value<string>();
                var toolResult = tool(JObject.Parse(arguments));
                Messages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = Newtonsoft.Json.JsonConvert.SerializeObject(toolResult)
                }));
            }

            return GenerateCompletion(Messages);
        }

        return null;
    }

    private static async Task<JObject?> Post(string apiEndpoint, string apiKey, string content)
    {
        using (HttpClient client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("api-key", apiKey);

            var body = new StringContent(content, System.Text.Encoding.UTF8, "application/json");
            try
            {
                var response = await client.PostAsync(apiEndpoint, body);
                var responseContent = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(responseContent);
                }
                else
                {
                    Console.WriteLine($"Error: {responseContent}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: {e}");
            }
        }

        return null;
    }

    public static string GetPayload(string model, List<JObject> messages, int maxTokens = -1, double temperature = 0.7, List<JObject>? tools = null, string toolChoice = "auto")
    {
        var payload = new JObject();
        payload.Add("model", model);
        payload.Add("messages", JArray.FromObject(messages));
        if (maxTokens > 0)
        {
            payload.Add("max_tokens", maxTokens);
        }
        payload.Add("temperature", temperature);
        if (tools != null && tools.Count > 0)
        {
            payload.Add("tools", JArray.FromObject(tools));
            payload.Add("tool_choice", toolChoice);
        }

        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }
}
