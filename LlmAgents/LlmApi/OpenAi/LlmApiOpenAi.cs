using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.Extensions.Logging;

namespace LlmAgents.LlmApi.OpenAi;

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

    public double Temperature { get; set; } = 1.0;

    public int MaxRetryOnThrottledAttempts { get; set; } = 3;

    public TimeSpan HttpTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    public async Task<ChatCompletionStreamParser?> GetStreamingCompletion(List<ChatCompletionMessageParam> messages, List<ChatCompletionFunctionTool>? tools = null, string toolChoice = "auto", bool outputReasoning = true, CancellationToken cancellationToken = default)
    {
        var stream = await GetCompletionStream(messages, tools, toolChoice, cancellationToken);
        if (stream == null)
        {
            return null;
        }

        var streamParser = new ChatCompletionStreamParser(stream) { OutputReasoning = outputReasoning };
        streamParser.Parse(cancellationToken);

        return streamParser;
    }

    private async Task<Stream?> GetCompletionStream(List<ChatCompletionMessageParam> messages, List<ChatCompletionFunctionTool>? tools = null, string toolChoice = "auto", CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count < 1)
        {
            throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
        }

        var payload = new ChatCompletionRequest(true)
        {
            Model = Model,
            Messages = messages,
            MaxCompletionTokens = MaxCompletionTokens,
            Temperature = Temperature,
            Tools = tools,
            ToolChoice = toolChoice,
        };

        return await GetCompletionStream(payload, retryAttempt: 0, cancellationToken);
    }

    private async Task<Stream?> GetCompletionStream(ChatCompletionRequest completionRequest, int retryAttempt = 0, CancellationToken cancellationToken = default)
    {
        var client = new HttpClient()
        {
            Timeout = HttpTimeout
        };

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoint)
        {
            Content = JsonContent.Create(completionRequest)
        };

        try
        {
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync(cancellationToken);
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            var errorResponse = JsonSerializer.Deserialize<ChatCompletionErrorResponse>(responseContent);
            if (errorResponse == null)
            {
                Log.LogError("Error deserializing error response: {responseContent}", responseContent);
                return null;
            }

            if (string.Equals("429", errorResponse.Error.Code) && retryAttempt < MaxRetryOnThrottledAttempts)
            {
                // default wait 30 seconds
                var seconds = 30 * (retryAttempt + 1);

                if (!string.IsNullOrEmpty(errorResponse.Error.Message))
                {
                    var pattern = @"retry\s+after\s+(\d+)\s+seconds";
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                    var match = regex.Match(errorResponse.Error.Message);
                    if (match.Success)
                    {
                        seconds = int.Parse(match.Groups[1].Value) + 5;
                    }
                }

                Log.LogInformation("Request throttled... waiting {seconds} seconds and retrying.", seconds);
                await Task.Delay(seconds * 1000, cancellationToken);
                return await GetCompletionStream(completionRequest, retryAttempt + 1, cancellationToken);
            }
            else
            {
                Log.LogError("Error while geting chat completion: {message}", errorResponse.Error.Message);
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting completion stream");
        }

        return null;
    }
}
