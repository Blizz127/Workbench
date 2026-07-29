using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Workbench.Data;

/// <summary>Shared conversion helpers between CLR values and their SQLite representations.</summary>
internal static class SqliteMapping
{
    public static string ToDb(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static object ToDb(DateTimeOffset? value) =>
        value is null ? DBNull.Value : value.Value.ToString("O", CultureInfo.InvariantCulture);

    public static object ToDbNullable(string? value) => (object?)value ?? DBNull.Value;

    public static object ToDbNullable(int? value) => (object?)value ?? DBNull.Value;

    public static string ToJson(List<string> values) => JsonSerializer.Serialize(values);

    public static List<string> FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static DateTimeOffset GetDateTimeOffset(this SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public static DateTimeOffset? GetNullableDateTimeOffset(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDateTimeOffset(ordinal);

    public static string? GetNullableString(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static int? GetNullableInt32(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static TEnum GetEnum<TEnum>(this SqliteDataReader reader, int ordinal) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(reader.GetString(ordinal), ignoreCase: true, out var parsed) ? parsed : default;
}
