using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmAgents.LlmApi.OpenAi.ChatCompletion;

public class ChatCompletionFunctionTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required ChatCompletionFunctionDefinition Function { get; set; }
}

public class ChatCompletionFunctionDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatCompletionFunctionParameters? Parameters { get; set; }

    [JsonPropertyName("strict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Strict { get; set; }
}

public class ChatCompletionFunctionParameters
{
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonSchemaType? Type { get; set; } = JsonSchemaType.Object;

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ChatCompletionFunctionParameter>? Properties { get; set; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }

    [JsonPropertyName("additionalProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AdditionalProperties { get; set; }

    [JsonPropertyName("$schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; set; }
}

/// <summary>
/// Represents a JSON Schema type, which can be either a single type string or an array of type strings.
/// </summary>
[JsonConverter(typeof(JsonSchemaTypeConverter))]
public class JsonSchemaType
{
    public string[] Types { get; set; } = [];

    public bool IsSingleType => Types.Length == 1;

    public string? SingleType => IsSingleType ? Types[0] : null;

    public static readonly JsonSchemaType Object = JsonSchemaType.FromString("object");
    public static readonly JsonSchemaType Array = JsonSchemaType.FromString("array");
    public static readonly JsonSchemaType String = JsonSchemaType.FromString("string");
    public static readonly JsonSchemaType Number = JsonSchemaType.FromString("number");
    public static readonly JsonSchemaType Integer = JsonSchemaType.FromString("integer");
    public static readonly JsonSchemaType Boolean = JsonSchemaType.FromString("boolean");
    public static readonly JsonSchemaType Null = JsonSchemaType.FromString("null");

    public static JsonSchemaType FromString(string type) => new() { Types = [type] };

    public static JsonSchemaType FromArray(string[] types) => new() { Types = types };

    public static implicit operator JsonSchemaType(string type) => FromString(type);

    public override string ToString() => IsSingleType ? SingleType! : string.Join(", ", Types);
}

public class JsonSchemaTypeConverter : JsonConverter<JsonSchemaType>
{
    public override JsonSchemaType? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return JsonSchemaType.FromString(reader.GetString()!);
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            var types = new List<string>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }
                if (reader.TokenType == JsonTokenType.String)
                {
                    types.Add(reader.GetString()!);
                }
            }
            return JsonSchemaType.FromArray(types.ToArray());
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        throw new JsonException($"Expected string or array for JsonSchemaType, got {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, JsonSchemaType value, JsonSerializerOptions options)
    {
        if (value.IsSingleType)
        {
            writer.WriteStringValue(value.SingleType);
        }
        else
        {
            writer.WriteStartArray();
            foreach (var type in value.Types)
            {
                writer.WriteStringValue(type);
            }
            writer.WriteEndArray();
        }
    }
}

public class ChatCompletionFunctionParameter
{
    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonSchemaType? Type { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChatCompletionFunctionParameter? Items { get; set; }

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, ChatCompletionFunctionParameter>? Properties { get; set; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }

    [JsonPropertyName("additionalProperties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AdditionalProperties { get; set; }
}
