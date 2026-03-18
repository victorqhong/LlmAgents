namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

using System.Text.Json.Serialization;

public class ChatCompletionContentPartImage : IChatCompletionContentPart
{
    [JsonPropertyName("image_url")]
    public required ChatCompletionContentPartImageUrl ImageUrl { get; set; }

    [JsonPropertyName("type")]
    public required string Type { get; set; } = "image_url";
}

public class ChatCompletionContentPartImageUrl
{
    [JsonPropertyName("url")]
    public required string Url { get; set; }
}
