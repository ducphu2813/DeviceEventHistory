using System.Globalization;
using System.Text.Json;

namespace DeviceEventHistory.Application.AppHub.Mapping;

internal static class AppHubJsonValueReader
{
    public static bool TryGetProperty(
        JsonElement payload,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public static int? ReadInt32(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : value.ValueKind == JsonValueKind.String
                && int.TryParse(
                    value.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var textNumber)
                    ? textNumber
                    : null;

    public static string? ReadString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? value.ToString()
                : null;

    public static bool? ReadBoolean(JsonElement value) =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : value.ValueKind == JsonValueKind.String
                && bool.TryParse(value.GetString(), out var textValue)
                ? textValue
                : null;

    public static double? ReadDouble(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : value.ValueKind == JsonValueKind.String
                && double.TryParse(
                    value.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var textNumber)
                    ? textNumber
                    : null;

    public static DateTimeOffset? ReadLocalDateTime(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var parsedDateTime))
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(parsedDateTime.DateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeSpan.Zero);
    }
}
