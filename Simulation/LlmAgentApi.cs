namespace Simulation;

using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

public class LlmAgentApi
{
    private readonly ILogger log = Program.loggerFactory.CreateLogger(nameof(LlmAgentApi));

    private readonly List<Tool> Tools = [];
    private readonly List<JObject> ToolDefinitions = [];
    private readonly Dictionary<string, Tool> ToolMap = [];

    public LlmAgentApi(string apiEndpoint, string apiKey, string model, string? systemPrompt = null)
        : this(apiEndpoint, apiKey, model, (List<JObject>?)null)
    {
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            Messages.Add(JObject.FromObject(new { role = "system", content = systemPrompt }));
        }
    }

    public LlmAgentApi(string apiEndpoint, string apiKey, string model, List<JObject>? messages = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiEndpoint);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        ApiEndpoint = apiEndpoint;
        ApiKey = apiKey;
        Model = model;

        if (messages != null)
        {
            Messages = messages;
        }
    }

    public List<JObject> Messages { get; private set; } = [];

    public string ApiEndpoint { get; private set; }

    public string ApiKey { get; private set; }

    public string Model { get; set; }

    public int MaxTokens { get; set; } = 1024;

    public double Temperature { get; set; } = 0.7;

    public void AddTool(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        Tools.Add(tool);
        ToolDefinitions.Add(tool.Schema);
        ToolMap.Add(tool.Name, tool);
    }

    public string GenerateCompletion(string userMessage)
    {
        ArgumentException.ThrowIfNullOrEmpty(userMessage);

        Messages.Add(JObject.FromObject(new { role = "user", content = userMessage }));
        return GenerateCompletion(Messages);
    }

    public string GenerateCompletion(List<JObject> messages)
    {
        if (messages == null || messages.Count < 1)
        {
            throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
        }

        var payload = GetPayload(Model, messages, MaxTokens, Temperature, ToolDefinitions, "auto");

        var completion = Post(ApiEndpoint, ApiKey, payload).ConfigureAwait(false).GetAwaiter().GetResult();
        if (completion == null)
        {
            throw new ApplicationException("Could not retrieve completion");
        }

        var result = ProcessCompletion(completion);
        if (string.IsNullOrEmpty(result))
        {
            throw new ApplicationException("Could not process completion");
        }

        return result;
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
        if (string.Equals(finishReason, "stop") || string.Equals(finishReason, "length"))
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

                var tool = ToolMap[name].Function;

                var arguments = function["arguments"]?.Value<string>();
                if (arguments == null)
                {
                    return null;
                }

                log.LogInformation($"Calling tool: {name}");

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
        else
        {
            throw new NotImplementedException(finishReason);
        }
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
