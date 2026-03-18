using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

[JsonConverter(typeof(ChatCompletionContentPartConverter))]
public interface IChatCompletionContentPart { }

public class ChatCompletionContentPartConverter : JsonConverter<IChatCompletionContentPart>
{
    public override IChatCompletionContentPart? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        if (!doc.RootElement.TryGetProperty("type", out var typeProperty))
        {
            throw new JsonException();
        }

        var type = typeProperty.GetString();

        if (string.Equals(type, "text"))
        {
            return doc.Deserialize<ChatCompletionContentPartText>() ?? throw new JsonException();
        }
        else if (string.Equals(type, "image_url"))
        {
            return doc.Deserialize<ChatCompletionContentPartImage>() ?? throw new JsonException();
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, IChatCompletionContentPart value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            throw new NullReferenceException();
        }

        if (value is ChatCompletionContentPartText textContent)
        {
            JsonSerializer.Serialize(writer, textContent, options);
        }
        else if (value is ChatCompletionContentPartImage imageContent)
        {
            JsonSerializer.Serialize(writer, imageContent, options);
        }
        else
        {
            throw new JsonException();
        }
    }
}
