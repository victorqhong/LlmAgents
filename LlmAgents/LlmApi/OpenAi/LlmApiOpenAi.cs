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

    public HttpClient HttpClient { get; set; }

    public LlmApiOpenAi(ILoggerFactory loggerFactory, LlmApiConfig llmApiConfig)
        : this(loggerFactory, llmApiConfig, new HttpClient())
    {
        HttpClient.Timeout = HttpTimeout;
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiConfig.ApiKey}");
        HttpClient.DefaultRequestHeaders.Add("Accept", "text/event-stream");
    }

    public LlmApiOpenAi(ILoggerFactory loggerFactory, LlmApiConfig llmApiConfig, HttpClient httpClient)
    {
        Log = loggerFactory.CreateLogger(nameof(LlmApiOpenAi));
        ApiConfig = llmApiConfig;
        HttpClient = httpClient;
    }

    public int MaxRetryOnThrottledAttempts { get; set; } = 3;

    public TimeSpan HttpTimeout { get; set; } = Timeout.InfiniteTimeSpan;

    public async Task<CompletionHttpResult> GetStreamingCompletion(List<ChatCompletionMessageParam> messages, List<ChatCompletionFunctionTool>? tools = null, string toolChoice = "auto", CancellationToken cancellationToken = default)
    {
        if (messages == null || messages.Count < 1)
        {
            throw new ArgumentException($"{nameof(messages)} is null or doesn't contain messages", nameof(messages));
        }

        var payload = CreateChatCompletionRequest(messages, ApiConfig.Temperature, ApiConfig.MaxCompletionTokens, tools, toolChoice);
        return await GetStreamingCompletion(payload, cancellationToken);
    }

    public async Task<CompletionHttpResult> GetStreamingCompletion(ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        return await GetCompletionStream(request, retryAttempt: 0, cancellationToken);
    }

    protected async Task<CompletionHttpResult> GetCompletionStream(ChatCompletionRequest completionRequest, int retryAttempt = 0, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiConfig.ApiEndpoint)
        {
            Content = JsonContent.Create(completionRequest)
        };

        HttpResponseMessage? response;
        try
        {
            response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while sending request");
            return CompletionHttpResult.ConnectionError();
        }

        try
        {
            if (response.IsSuccessStatusCode)
            {
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return CompletionHttpResult.Success(stream);
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
                return CompletionHttpResult.OtherError();
            }

            if (string.Equals("429", errorResponse.Error.Code) || string.Equals("too_many_requests", errorResponse.Error.Code) || (string.Equals("too_many_requests", errorResponse.Error.Type) && string.Equals("rate_limit_exceeded", errorResponse.Error.Code))
            )
            {
                if (retryAttempt < MaxRetryOnThrottledAttempts)
                {
                    var seconds = 10 * (retryAttempt + 1);

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
                    return CompletionHttpResult.ThrottledError();
                }
            }
            else
            {
                Log.LogError("Error while getting chat completion: {message}, code: {code}, type: {type}", errorResponse.Error.Message, errorResponse.Error.Code, errorResponse.Error.Type);
                return CompletionHttpResult.OtherError();
            }
        }
        catch (Exception e)
        {
            Log.LogError(e, "Exception while getting completion stream");
        }

        return CompletionHttpResult.OtherError();
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

    public class CompletionHttpResult
    {
        public readonly Stream? CompletionStream;
        public readonly CompletionError? Error;

        public CompletionHttpResult(Stream? completionStream, CompletionError? error)
        {
            CompletionStream = completionStream;
            Error = error;
        }

        public static CompletionHttpResult Success(Stream completionStream)
        {
            return new CompletionHttpResult(completionStream, null);
        }

        public static CompletionHttpResult ThrottledError()
        {
            return new CompletionHttpResult(null, CompletionError.Throttled);
        }

        public static CompletionHttpResult ConnectionError()
        {
            return new CompletionHttpResult(null, CompletionError.ConnectionError);
        }

        public static CompletionHttpResult OtherError()
        {
            return new CompletionHttpResult(null, CompletionError.Other);
        }

        public enum CompletionError
        {
            Throttled,
            ConnectionError,
            Other
        }
    }
}
