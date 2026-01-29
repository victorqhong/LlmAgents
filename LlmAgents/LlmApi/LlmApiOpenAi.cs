using LlmAgents.LlmApi.Content;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LlmAgents.LlmApi;

public class LlmApiOpenAi
{
    private readonly ILogger Log;

    public LlmApiOpenAi(ILoggerFactory loggerFactory, LlmApiOpenAiParameters parameters)
        : this(loggerFactory, parameters.ApiEndpoint, parameters.ApiKey, parameters.ApiModel)
    {
        ContextSize = parameters.ContextSize;
        MaxCompletionTokens = parameters.MaxCompletionTokens;
    }

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

    public int MaxRetryOnThrottledAttempts { get; set; } = 3;

    public async Task<LlmApiOpenAiStreamingCompletionParser?> GetStreamingCompletion(IList<JObject> messages, IList<JObject>? tools = null, string toolChoice = "auto", CancellationToken cancellationToken = default)
    {
        var stream = await GetCompletionStream(messages, tools, toolChoice, cancellationToken).ConfigureAwait(false);
        if (stream == null)
        {
            return null;
        }

        var streamParser = new LlmApiOpenAiStreamingCompletionParser(stream);
        streamParser.Parse(cancellationToken);

        return streamParser;
    }

    private async Task<Stream?> GetCompletionStream(IList<JObject> messages, IList<JObject>? tools = null, string toolChoice = "auto", CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count < 1)
        {
            throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var payload = GetPayload(Model, messages, MaxCompletionTokens ?? 8192, Temperature, tools, toolChoice, true);

        return await GetCompletionStream(ApiEndpoint, ApiKey, payload, retryAttempt: 0, cancellationToken);
    }

    private async Task<Stream?> GetCompletionStream(string apiEndpoint, string apiKey, string content, int retryAttempt = 0, CancellationToken cancellationToken = default)
    {
        HttpClient client = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, apiEndpoint)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync(cancellationToken);
            }
            else
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json"))
                {
                    var responseMessage = JObject.Parse(responseContent);

                    var error = responseMessage.Value<JObject>("error");
                    if (error != null)
                    {
                        var message = error.Value<string>("message");
                        var code = error.Value<string>("code");
                        if (string.Equals("429", code) && retryAttempt < MaxRetryOnThrottledAttempts)
                        {
                            // default wait 30 seconds
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
                            await Task.Delay(seconds * 1000, cancellationToken);
                            return await GetCompletionStream(apiEndpoint, apiKey, content, retryAttempt + 1, cancellationToken);
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

    public static string GetPayload(string model, IList<JObject> messages, int maxCompletionTokens, double temperature, IList<JObject>? tools = null, string toolChoice = "auto", bool stream = true)
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
        if (stream)
        {
            payload.Add("stream_options", new JObject
            {
                { "include_usage", true }
            });
        }

        return payload.ToString(Newtonsoft.Json.Formatting.None);
    }
}
