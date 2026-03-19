using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "role")]
[JsonDerivedType(typeof(ChatCompletionMessageParamAssistant), "assistant")]
[JsonDerivedType(typeof(ChatCompletionMessageParamTool), "tool")]
[JsonDerivedType(typeof(ChatCompletionMessageParamUser), "user")]
[JsonDerivedType(typeof(ChatCompletionMessageParamSystem), "system")]
public abstract class ChatCompletionMessageParam
{
    [JsonPropertyName("content")]
    public IChatCompletionMessageParamContent? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasoningContent { get; set; }
}

[JsonConverter(typeof(ChatCompletionMessageParamContentConverter))]
public interface IChatCompletionMessageParamContent { }

public class ChatCompletionMessageParamContentParts : IChatCompletionMessageParamContent
{
    [JsonPropertyName("content")]
    public required List<IChatCompletionContentPart> Content { get; set; } = [];
}

public class ChatCompletionMessageParamContentString : IChatCompletionMessageParamContent
{
    [JsonPropertyName("content")]
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
            var contentParts = JsonSerializer.Deserialize<List<IChatCompletionContentPart>>(ref reader, options) ?? throw new JsonException();
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

