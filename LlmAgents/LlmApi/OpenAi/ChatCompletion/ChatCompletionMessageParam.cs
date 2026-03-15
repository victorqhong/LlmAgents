using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionMessageParam
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    [JsonConverter(typeof(ChatCompletionMessageParamContentConverter))]
    public required IChatCompletionMessageParamContent Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }
}

public interface IChatCompletionMessageParamContent { }

public class ChatCompletionMessageParamContentParts : IChatCompletionMessageParamContent
{
    public required List<ChatCompletionContentPart> Content { get; set; } = [];
}

public class ChatCompletionMessageParamContentString : IChatCompletionMessageParamContent
{
    public required string Content { get; set; }
}

public class ChatCompletionMessageParamContentConverter : JsonConverter<IChatCompletionMessageParamContent>
{
    public override IChatCompletionMessageParamContent? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is string content)
        {
            return new ChatCompletionMessageParamContentString
            {
                Content = content
            };
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            var contentParts = JsonSerializer.Deserialize<List<ChatCompletionContentPart>>(ref reader, options) ?? throw new JsonException();
            return new ChatCompletionMessageParamContentParts
            {
                Content = contentParts
            };
        }

        throw new NotImplementedException(reader.TokenType.ToString());
    }

    public override void Write(Utf8JsonWriter writer, IChatCompletionMessageParamContent value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            throw new NullReferenceException();
        }

        if (value is ChatCompletionMessageParamContentString stringContent)
        {
            JsonSerializer.Serialize(writer, stringContent.Content, stringContent.Content.GetType(), options);
        }
        else if (value is ChatCompletionMessageParamContentParts partsContent)
        {
            JsonSerializer.Serialize(writer, partsContent.Content, partsContent.Content.GetType(), options);
        }
        else
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
