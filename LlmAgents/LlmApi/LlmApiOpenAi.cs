using LlmAgents.Agents;
﻿using LlmAgents.LlmApi.Content;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Text;

namespace LlmAgents.LlmApi;

public class LlmApiOpenAi : ILlmApiMessageProvider
{
    private readonly ILogger Log;

    public LlmApiOpenAi(ILoggerFactory loggerFactory, string apiEndpoint, string apiKey, string model, List<JObject>? messages = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiEndpoint);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        Log = loggerFactory.CreateLogger(nameof(LlmApiOpenAi));

        ApiEndpoint = apiEndpoint;
        ApiKey = apiKey;
        Model = model;

        if (messages != null)
        {
            Messages.AddRange(messages);
        }
    }

    public LlmAgent? Agent { get; set; }

    public List<JObject> Messages { get; private set; } = [];

    public string ApiEndpoint { get; private set; }

    public string ApiKey { get; private set; }

    public string Model { get; set; }

    public int MaxCompletionTokens { get; set; } = 2048;

    public int TargetContextSize { get; set; } = 51200;

    public double Temperature { get; set; } = 0.7;

    public string? FinishReason { get; private set; }

    public int UsageCompletionTokens { get; private set; }

    public int UsagePromptTokens { get; private set; }

    public int UsageTotalTokens { get; private set; }

    public event Action<TokenUsage>? PostParseUsage;

    public async Task<string?> GenerateCompletion(IEnumerable<IMessageContent> messageContents, CancellationToken cancellationToken = default)
    {
        var completion = await GenerateStreamingCompletion(messageContents, cancellationToken);
        if (completion == null)
        {
            return null;
        }

        var sb = new StringBuilder();
        await foreach (var chunk in completion)
        {
            sb.Append(chunk);
        }

        return sb.ToString();
    }

    public async Task<IAsyncEnumerable<string>?> GenerateStreamingCompletion(IEnumerable<IMessageContent> messageContents, CancellationToken cancellationToken = default)
    {
        var message = GetMessage(messageContents);
        Messages.Add(message);

        return await GenerateStreamingCompletion(Messages, cancellationToken);
    }

    public async Task<IAsyncEnumerable<string>?> GenerateStreamingCompletion(List<JObject> messages, CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count < 1)
        {
            throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var payload = GetPayload(Model, messages, MaxCompletionTokens, Temperature, Agent?.GetToolDefinitions(), "auto", true);

        return await GetCompletion(ApiEndpoint, ApiKey, payload, retryAttempt: 0, cancellationToken);
    }

    private async Task<IAsyncEnumerable<string>?> GetCompletion(string apiEndpoint, string apiKey, string content, int retryAttempt = 0, CancellationToken cancellationToken = default)
    {
        using HttpClient client = new();
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, apiEndpoint)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                return ParseCompletion(stream, cancellationToken);
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json"))
                {
                    var responseMessage = JObject.Parse(responseContent);

                    var error = responseMessage.Value<JObject>("error");
                    if (error != null)
                    {
                        var message = error.Value<string>("message");
                        var code = error.Value<string>("code");
                        if (string.Equals("429", code) && retryAttempt < 3)
                        {
                            var seconds = 30 * (retryAttempt + 1);

                            if (!string.IsNullOrEmpty(message))
                            {
                                var pattern = @"retry\s+after\s+(\d+)\s+seconds";
                                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                                var match = regex.Match(message);
                                if (match.Success)
                                {
                                    seconds = int.Parse(match.Groups[1].Value) + 5;
                                }
                            }

                            Log.LogInformation("Request throttled... waiting {seconds} seconds and retrying.", seconds);
                            Thread.Sleep(seconds * 1000);
                            return await GetCompletion(apiEndpoint, apiKey, content, retryAttempt + 1, cancellationToken);
                        }
                        else
                        {
                            Log.LogError("Error: {message}", message);
                        }
                    }
                    else
                    {
                        Log.LogError("Error: {responseContent}", responseContent);
                    }
                }
                else
                {
                    Log.LogError("Error: {responseContent}", responseContent);
                }
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Got exception");
        }

        return null;
    }

    private async IAsyncEnumerable<string> ParseCompletion(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? role = null;
        string? finishReason = null;
        var parsedToolCalls = new Dictionary<int, Dictionary<string, string>>();
        var contentBuffer = new StringBuilder();
        var reasoningContentBuffer = new StringBuilder();

        var seenReasoningContent = false;
        var seenContent = false;

        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            else if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            else if ("data: [DONE]".Equals(line))
            {
                break;
            }

            var data = line[6..];
            var json = JObject.Parse(data);

            if (!json.ContainsKey("object") || json.Value<string>("object") is not string @object || !"chat.completion.chunk".Equals(@object))
            {
                continue;
            }

            if (json.ContainsKey("usage") && json.Value<JObject>("usage") is JObject usage)
            {
                UsageCompletionTokens = usage.Value<int>("completion_tokens");
                UsagePromptTokens = usage.Value<int>("prompt_tokens");
                UsageTotalTokens = usage.Value<int>("total_tokens");

                PostParseUsage?.Invoke(new TokenUsage
                {
                    PromptTokens = UsagePromptTokens,
                    CompletionTokens = UsageCompletionTokens,
                    TotalTokens = UsageTotalTokens,
                });
            }

            // by default just use the first choice, technically there may be more
            if (!json.ContainsKey("choices") || json.Value<JArray>("choices") is not JArray choices || choices.Count < 1 || choices[0] is not JObject choice)
            {
                continue;
            }

            if (string.IsNullOrEmpty(finishReason))
            {
                finishReason = choice.Value<string>("finish_reason");
            }

            if (!choice.ContainsKey("delta") || choice.Value<JObject>("delta") is not JObject delta)
            {
                continue;
            }

            if (string.IsNullOrEmpty(role))
            {
                role = delta.Value<string>("role");
            }

            if (delta.ContainsKey("reasoning_content") && delta.Value<string>("reasoning_content") is string deltaReasoningContent)
            {
                if (!seenReasoningContent)
                {
                    yield return "<thinking>";
                    seenReasoningContent = true;
                }

                yield return deltaReasoningContent;
                reasoningContentBuffer.Append(deltaReasoningContent);
            }

            if (delta.ContainsKey("content") && delta.Value<string>("content") is string deltaContent)
            {
                if (seenReasoningContent && !seenContent)
                {
                    yield return "</thinking>\n";
                    seenContent = true;
                }

                yield return deltaContent;
                contentBuffer.Append(deltaContent);
            }

            if (delta.ContainsKey("tool_calls") && delta.Value<JArray>("tool_calls") is JArray toolCalls)
            {
                foreach (var element in toolCalls)
                {
                    if (element is not JObject toolCall)
                    {
                        continue;
                    }

                    if (!toolCall.ContainsKey("index") || toolCall.Value<int?>("index") is not int index)
                    {
                        continue;
                    }

                    if (!parsedToolCalls.TryGetValue(index, out Dictionary<string, string>? toolCallData))
                    {
                        toolCallData = [];
                        parsedToolCalls.Add(index, toolCallData);
                    }

                    if (toolCall.ContainsKey("id") && toolCall.Value<string>("id") is string id && !toolCallData.ContainsKey("id"))
                    {
                        toolCallData.Add("id", id);
                    }

                    if (toolCall.ContainsKey("type") && toolCall.Value<string>("type") is string type && !toolCallData.ContainsKey("type"))
                    {
                        toolCallData.Add("type", type);
                    }

                    if (toolCall.ContainsKey("function") && toolCall.Value<JObject>("function") is JObject function)
                    {
                        if (function.ContainsKey("name") && function.Value<string>("name") is string functionName && !toolCallData.ContainsKey("functionName"))
                        {
                            toolCallData.Add("functionName", functionName);
                        }

                        if (function.ContainsKey("arguments") && function.Value<string>("arguments") is string functionArguments)
                        {
                            if (!toolCallData.TryAdd("functionArguments", functionArguments))
                            {
                                toolCallData["functionArguments"] += functionArguments;
                            }
                        }
                    }
                }
            }
        }

        if (seenReasoningContent && !seenContent)
        {
            yield return "</thinking>\n";
            seenContent = true;
        }
        
        role ??= "assistant";
        FinishReason = finishReason;

        var content = contentBuffer.ToString();
        var reasoningContent = reasoningContentBuffer.ToString();

        if (string.Equals(FinishReason, "stop"))
        {
            Messages.Add(JObject.FromObject(new { role, content }));
        }
        else if (string.Equals(FinishReason, "length"))
        {
            Messages.Add(JObject.FromObject(new { role, content }));
            var continuation = await GenerateStreamingCompletion(Messages, cancellationToken);
            if (continuation != null)
            {
                await foreach (var chunk in continuation)
                {
                    yield return chunk;
                }
            }
        }
        else if (string.Equals(FinishReason, "tool_calls"))
        {
            var toolCalls = parsedToolCalls.Select(kvp =>
            {
                return new
                {
                    id = kvp.Value["id"],
                    type = kvp.Value["type"],
                    function = new
                    {
                        name = kvp.Value["functionName"],
                        arguments = kvp.Value["functionArguments"]
                    }
                };
            });

            Messages.Add(JObject.FromObject(new { role, content, tool_calls = toolCalls }));

            foreach (var toolCall in toolCalls)
            {
                var id = toolCall.id;

                if (Agent == null)
                {
                    Messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        content = $"Invalid tool call: tools are not available"
                    }));

                    continue;
                }

                var function = toolCall.function;
                if (function == null)
                {
                    Messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        content = $"Invalid tool call: tool call does not contain 'function' property"
                    }));

                    continue;
                }

                var name = function.name;
                if (string.IsNullOrEmpty(name))
                {
                    Messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        name,
                        content = $"Invalid tool call: tool call does not contain 'name' property"
                    }));

                    continue;
                }

                var arguments = function.arguments;
                if (arguments == null)
                {
                    Messages.Add(JObject.FromObject(new
                    {
                        role = "tool",
                        tool_call_id = id,
                        name,
                        content = $"Invalid tool call: tool call does not contain 'arguments' property"
                    }));

                    continue;
                }

                Log.LogInformation("Calling tool '{name}' with arguments '{arguments}'", name, arguments);

                string toolContent;
                try
                {
                    var toolResult = await Agent.CallTool(name, JObject.Parse(arguments));
                    if (toolResult == null)
                    {
                        Messages.Add(JObject.FromObject(new
                        {
                            role = "tool",
                            tool_call_id = id,
                            name,
                            content = $"Invalid tool call: tool {name} could not be found"
                        }));

                        continue;
                    }

                    toolContent = Newtonsoft.Json.JsonConvert.SerializeObject(toolResult);
                }
                catch (Exception ex)
                {
                    toolContent = $"Got exception: {ex.Message}";
                }

                Messages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = toolContent
                }));
            }

            var toolCompletion = await GenerateStreamingCompletion(Messages, cancellationToken);
            if (toolCompletion != null)
            {
                await foreach (var chunk in toolCompletion)
                {
                    yield return chunk;
                }
            }
        }
        else
        {
            throw new NotImplementedException(FinishReason);
        }
    }

    public static JObject GetMessage(IEnumerable<IMessageContent> messageContents)
    {
        ArgumentNullException.ThrowIfNull(messageContents);

        var content = new JArray();

        foreach (var messageContent in messageContents)
        {
            if (messageContent is MessageContentText userMessage)
            {
                var textContent = new JObject();
                textContent.Add("type", "text");
                textContent.Add("text", userMessage.Text);
                content.Add(textContent);

            }
            else if (messageContent is MessageContentImageUrl imageUrl)
            {
                var url = string.Format("data:{0};base64,{1}", imageUrl.MimeType, imageUrl.DataBase64);

                var imageContent = new JObject();
                imageContent.Add("type", "image_url");
                imageContent.Add("image_url", JObject.FromObject(new { url }));
                content.Add(imageContent);
            }
        }

        var message = new JObject();
        message.Add("role", "user");
        message.Add("content", content);

        return message;
    }

    public static string GetPayload(string model, List<JObject> messages, int maxCompletionTokens, double temperature, IReadOnlyList<JObject>? tools = null, string toolChoice = "auto", bool stream = true)
    {
        var payload = new JObject();
        payload.Add("model", model);
        payload.Add("messages", JArray.FromObject(messages));
        payload.Add("max_completion_tokens", maxCompletionTokens);
        payload.Add("temperature", temperature);
        if (tools != null && tools.Count > 0)
        {
            payload.Add("tools", JArray.FromObject(tools));
            payload.Add("tool_choice", toolChoice);
        }

        payload.Add("stream", stream);

        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    public Task<int> CountMessages()
    {
        return Task.FromResult(Messages.Count);
    }

    public Task PruneContext(int numMessagesToKeep)
    {
        if (Messages.Count <= numMessagesToKeep)
        {
            return Task.CompletedTask;
        }

        // keep the first message (system prompt) and last message (a tool call is needed before a tool result)
        var pruneCount = Math.Max(Messages.Count - numMessagesToKeep, 0) - 1;
        Messages.RemoveRange(1, pruneCount - 1);

        // remove messages from the beginning to maintain a well-formatted message list
        while (Messages.Count > 1 && !string.Equals(Messages[0].Value<string>("role"), "user") && !string.Equals(Messages[0].Value<string>("role"), "system"))
        {
            Messages.RemoveAt(1);
        }

        return Task.CompletedTask;
    }
}
