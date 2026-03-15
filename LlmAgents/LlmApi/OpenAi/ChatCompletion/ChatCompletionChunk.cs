using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionChunk
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("choices")]
    public required List<ChatCompletionChunkChoice> Choices { get; set; }

    [JsonPropertyName("created")]
    public required long Created { get; set; }

    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("usage")]
    public ChatCompletionUsage? Usage { get; set; }

    [JsonPropertyName("object")]
    public required string Object { get; set; }
}

public class ChatCompletionChunkChoice
{
    [JsonPropertyName("index")]
    public required int Index { get; set; }

    [JsonPropertyName("finish_reason")]
    public ChatCompletionChoiceFinishReason? FinishReason { get; set; }

    [JsonPropertyName("delta")]
    public required ChatCompletionChunkDelta Delta { get; set; }
}

public class ChatCompletionChunkDelta
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<ChatCompletionChunkToolCalls>? ToolCalls { get; set; }
}

public class ChatCompletionChunkToolCalls
{
    [JsonPropertyName("index")]
    public required int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("function")]
    public ChatCompletionChunkToolCallsFunction? Function { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; } = "function";
}

public class ChatCompletionChunkToolCallsFunction
{
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChatCompletionChoiceFinishReason
{
    [JsonStringEnumMemberName("stop")]
    Stop,

    [JsonStringEnumMemberName("length")]
    Length,

    [JsonStringEnumMemberName("tool_calls")]
    ToolCalls,

    [JsonStringEnumMemberName("content_filter")]
    ContentFilter,

    [JsonStringEnumMemberName("function_call")]
    FunctionCall,
}
