using LlmAgents.Agents;
using LlmAgents.LlmApi.Content;
using LlmAgents.Tools;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace LlmAgents.LlmApi;

public class LlmApiOpenAi : ILlmApiMessageProvider
{
    private readonly ILogger Log;

    public LlmApiOpenAi(ILoggerFactory loggerFactory, string apiEndpoint, string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrEmpty(apiEndpoint);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);
        ArgumentException.ThrowIfNullOrEmpty(model);

        Log = loggerFactory.CreateLogger(nameof(LlmApiOpenAi));

        ApiEndpoint = apiEndpoint;
        ApiKey = apiKey;
        Model = model;
    }

    public string ApiEndpoint { get; private set; }

    public string ApiKey { get; private set; }

    public string Model { get; set; }

    public int? MaxCompletionTokens { get; set; } = null;

    public int ContextSize { get; set; } = 8192;

    public double Temperature { get; set; } = 0.7;

    public string? FinishReason { get; private set; }

    public int UsageCompletionTokens { get; private set; }

    public int UsagePromptTokens { get; private set; }

    public int UsageTotalTokens { get; private set; }

    public event Action<TokenUsage>? PostParseUsage;

    public async Task<string?> GenerateCompletion(List<JObject> messages, List<Tool> tools, string toolChoice, CancellationToken cancellationToken = default)
    {
        var completion = await GenerateStreamingCompletion(messages, tools, toolChoice, cancellationToken);
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

    public async Task<IAsyncEnumerable<string>?> GenerateStreamingCompletion(List<JObject> messages, List<Tool> tools, string toolChoice, CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count < 1)
        {
            throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return await GetCompletion(messages, tools, toolChoice, retryAttempt: 0, cancellationToken);
    }

    private async Task<IAsyncEnumerable<string>?> GetCompletion(List<JObject> messages, List<Tool> tools, string toolChoice, int retryAttempt = 0, CancellationToken cancellationToken = default)
    {
        var content = GetPayload(Model, messages, MaxCompletionTokens ?? 8196, Temperature, tools, toolChoice, true);

        using HttpClient client = new();
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint)
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

                return ParseCompletion(stream, messages, tools, toolChoice, cancellationToken);
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
                    if (responseMessage.ContainsKey("error") && responseMessage.Value<JObject>("error") is JObject error)
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
                            return await GetCompletion(messages, tools, toolChoice, retryAttempt + 1, cancellationToken);
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

    private async IAsyncEnumerable<string> ParseCompletion(Stream stream, List<JObject> messages, List<Tool> tools, string toolChoice, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? role = null;
        string? finishReason = null;
        var parsedToolCalls = new Dictionary<int, Dictionary<string, string>>();
        var contentBuffer = new StringBuilder();
        var reasoningContentBuffer = new StringBuilder();

        var seenReasoningContent = false;
        var seenContent = false;

        StreamReader? reader = null;
        try
        {
            reader = new StreamReader(stream);
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
        }
        finally
        {
            if (reader != null)
            {
                reader.Close();
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
            messages.Add(JObject.FromObject(new { role, content }));
        }
        else if (string.Equals(FinishReason, "length"))
        {
            if (string.Equals(role, messages[^1].Value<string>("role")))
            {
                messages[^1]["content"] += content;
            }
            else
            {
                messages.Add(JObject.FromObject(new { role, content }));
            }

            var continuation = await GenerateStreamingCompletion(messages, tools, toolChoice, cancellationToken);
            if (continuation != null)
            {
                await foreach (var chunk in continuation)
                {
                    yield return chunk;
                }
            }

            if ("assistant".Equals(messages[^1].Value<string>("role")) && "assistant".Equals(messages[^2].Value<string>("role")))
            {
                var lastMessage = messages[^1].Value<string>("content");
                messages[^2]["content"] += lastMessage;
                messages.RemoveAt(messages.Count - 1);
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

            messages.Add(JObject.FromObject(new { role, content, tool_calls = toolCalls }));
        }
        else
        {
            Log.LogCritical("FinishReason '{FinishReason}' is not implemented", FinishReason);
            throw new NotImplementedException(FinishReason);
        }
    }

    public async Task ProcessToolCalls(List<JObject> messages, List<Tool> tools)
    {
        if (!string.Equals(FinishReason, "tool_calls"))
        {
            return;
        }

        var lastMessage = messages[^1];
        if (!lastMessage.ContainsKey("tool_calls") || lastMessage.Value<JArray>("tool_calls") is not JArray toolCalls)
        {
            return;
        }

        foreach (JObject toolCall in toolCalls)
        {
            var id = toolCall.Value<string>("id");

            var function = toolCall.Value<JObject>("function");
            if (function == null)
            {
                messages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    content = $"Invalid tool call: tool call does not contain 'function' property"
                }));

                continue;
            }

            var name = function.Value<string>("name");
            if (string.IsNullOrEmpty(name))
            {
                messages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = $"Invalid tool call: tool call does not contain 'name' property"
                }));

                continue;
            }

            var arguments = function.Value<string>("arguments");
            if (arguments == null)
            {
                messages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = $"Invalid tool call: tool call does not contain 'arguments' property"
                }));

                continue;
            }

            var tool = tools.Find(tool => string.Equals(tool.Name, name));
            if (tool == null)
            {
                messages.Add(JObject.FromObject(new
                {
                    role = "tool",
                    tool_call_id = id,
                    name,
                    content = $"Invalid tool call: tool {name} could not be found"
                }));

                continue;
            }

            Log.LogInformation("Calling tool '{name}' with arguments '{arguments}'", name, arguments);

            string toolContent;
            try
            {
                var toolResult = await tool.Invoke(JObject.Parse(arguments));
                toolContent = Newtonsoft.Json.JsonConvert.SerializeObject(toolResult);
            }
            catch (Exception ex)
            {
                toolContent = $"Got exception: {ex.Message}";
            }

            messages.Add(JObject.FromObject(new
            {
                role = "tool",
                tool_call_id = id,
                name,
                content = toolContent
            }));
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

    public static string GetPayload(string model, List<JObject> messages, int maxCompletionTokens, double temperature, List<Tool>? tools = null, string toolChoice = "auto", bool stream = true)
    {
        var payload = new JObject();
        payload.Add("model", model);
        payload.Add("messages", JArray.FromObject(messages));
        payload.Add("max_completion_tokens", maxCompletionTokens);
        payload.Add("temperature", temperature);
        if (tools != null && tools.Count > 0)
        {
            payload.Add("tools", JArray.FromObject(tools.Select(tool => tool.Schema)));

            JToken tool_choice;
            if (string.Equals(toolChoice, "auto"))
            {
                tool_choice = "auto";
            }
            else if (string.Equals(toolChoice, "required"))
            {
                tool_choice = "required";
            }
            else if (tools?.Exists(tool => string.Equals(toolChoice, tool.Name)) ?? false)
            {
                tool_choice = JObject.FromObject(new
                {
                    type = "function",
                    function = new
                    {
                        name = toolChoice
                    }
                });
            }
            else
            {
                tool_choice = "none";
            }

            payload.Add("tool_choice", tool_choice);
        }

        payload.Add("stream", stream);
        if (stream)
        {
            payload.Add("stream_options", new JObject
            {
                { "include_usage", true }
            });
        }

        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }

    public Task<int> CountMessages()
    {
        //return Task.FromResult(Messages.Count);
        return Task.FromResult(0);
    }

    public Task PruneContext(int numMessagesToKeep)
    {
        //// keep system prompt if present
        //var startIndex = 0;
        //if (string.Equals(Messages[0].Value<string>("role"), "system"))
        //{
        //    startIndex = 1;
        //    numMessagesToKeep = Math.Max(numMessagesToKeep, 1);
        //}

        //if (Messages.Count <= numMessagesToKeep)
        //{
        //    return Task.CompletedTask;
        //}

        //// remove messages from the beginning to maintain a well-formatted message list
        //var numMessagesToRemove = Messages.Count - numMessagesToKeep;
        //for (int i = 0; i < numMessagesToRemove; i++)
        //{
        //    if (string.Equals(Messages[startIndex].Value<string>("role"), "assistant") && startIndex == Messages.Count - 1)
        //    {
        //        continue;
        //    }
        //    else if (string.Equals(Messages[startIndex].Value<string>("role"), "user") && (startIndex == Messages.Count - 1 || startIndex == Messages.Count - 2 || startIndex == Messages.Count - 3))
        //    {
        //        continue;
        //    }

        //    Messages.RemoveAt(startIndex);
        //}

        return Task.CompletedTask;
    }
}
