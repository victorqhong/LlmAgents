using System.Text.Json;

namespace LlmAgents.Extensions;

public static class JsonDocumentExtensions
{
    public static bool TryGetValueString(this JsonDocument jsonDocument, string propertyName, string defaultValue, out string value)
    {
        if (!jsonDocument.RootElement.TryGetProperty(propertyName, out var property) || property.GetString() is not string propertyValue)
        {
            value = defaultValue;
            return false;
        }

        value = propertyValue;
        return true;
    }

    public static bool TryGetValueInt(this JsonDocument jsonDocument, string propertyName, out int? value)
    {
        if (!jsonDocument.RootElement.TryGetProperty(propertyName, out var property))
        {
            value = null;
            return false;
        }

        value = property.GetInt32();
        return true;
    }

    public static bool TryGetValueBool(this JsonDocument jsonDocument, string propertyName, bool defaultValue, out bool value)
    {
        if (!jsonDocument.RootElement.TryGetProperty(propertyName, out var property))
        {
            value = defaultValue;
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
