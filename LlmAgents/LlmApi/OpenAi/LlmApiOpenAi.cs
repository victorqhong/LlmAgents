using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using LlmAgents.Configuration;
using LlmAgents.LlmApi.OpenAi.ChatCompletion;
using Microsoft.Extensions.Logging;

namespace LlmAgents.LlmApi.OpenAi;

public class LlmApiOpenAi
{
    private readonly ILogger Log;

    public readonly LlmApiConfig ApiConfig;

    public LlmApiOpenAi(ILoggerFactory loggerFactory, LlmApiConfig llmApiConfig)
    {
        Log = loggerFactory.CreateLogger(nameof(LlmApiOpenAi));
        ApiConfig = llmApiConfig;
    }

    public int MaxRetryOnThrottledAttempts { get; set; } = 3;

    public TimeSpan HttpTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    public async Task<ChatCompletionStreamParser?> GetStreamingCompletion(List<ChatCompletionMessageParam> messages, List<ChatCompletionFunctionTool>? tools = null, string toolChoice = "auto", bool outputReasoning = true, CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count < 1)
        {
            throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
        }

        var payload = CreateChatCompletionRequest(messages, ApiConfig.Temperature, ApiConfig.MaxCompletionTokens, tools, toolChoice);
        return await GetStreamingCompletion(payload, outputReasoning, cancellationToken);
    }

    public async Task<ChatCompletionStreamParser?> GetStreamingCompletion(ChatCompletionRequest request, bool outputReasoning, CancellationToken cancellationToken)
    {
        var stream = await GetCompletionStream(request, retryAttempt: 0, cancellationToken);
        if (stream == null)
        {
            return null;
        }

        var streamParser = new ChatCompletionStreamParser(stream) { OutputReasoning = outputReasoning };
        streamParser.Parse(cancellationToken);

        return streamParser;
    }

    protected async Task<Stream?> GetCompletionStream(ChatCompletionRequest completionRequest, int retryAttempt = 0, CancellationToken cancellationToken = default)
    {
        var client = new HttpClient()
        {
            Timeout = HttpTimeout
        };

        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiConfig.ApiKey}");
        client.DefaultRequestHeaders.Add("Accept", "text/event-stream");

        var request = new HttpRequestMessage(HttpMethod.Post, ApiConfig.ApiEndpoint)
        {
            Content = JsonContent.Create(completionRequest)
        };

        HttpResponseMessage? response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while sending request");
            return null;
        }

        try
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync(cancellationToken);
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            ChatCompletionErrorResponse? errorResponse = null;
            try
            {
                errorResponse = JsonSerializer.Deserialize<ChatCompletionErrorResponse>(responseContent);
            }
            catch (Exception e)
            {
                Log.LogError(e, "Exception while deserializing error response");
            }

            if (errorResponse == null)
            {
                Log.LogError("Error response: {responseContent}", responseContent);
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

    protected virtual ChatCompletionRequest CreateChatCompletionRequest(List<ChatCompletionMessageParam> messages, double? temperature, int? maxCompletionTokens, List<ChatCompletionFunctionTool>? tools, string? toolChoice)
    {
        return new ChatCompletionRequest(true)
        {
            Model = ApiConfig.ApiModel,
            Messages = messages,
            MaxCompletionTokens = maxCompletionTokens,
            Temperature = temperature,
            Tools = tools,
            ToolChoice = toolChoice,
        };
    }
}
