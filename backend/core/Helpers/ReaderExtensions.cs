using Npgsql;

namespace Api.Helpers;

/// <summary>
/// Extension helpers for <see cref="NpgsqlDataReader"/> that make column access
/// by name concise and null-safe. Each helper performs one <c>GetOrdinal</c> lookup
/// and short-circuits to default values for nullable columns, eliminating the
/// repetitive <c>reader.IsDBNull(reader.GetOrdinal("x")) ? null : reader.GetString(reader.GetOrdinal("x"))</c>
/// pattern across repository mappers.
/// </summary>
public static class ReaderExtensions
{
    public static string GetString(this NpgsqlDataReader reader, string columnName)
        => reader.GetString(reader.GetOrdinal(columnName));

    public static string? GetNullableString(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static Guid GetGuid(this NpgsqlDataReader reader, string columnName)
        => reader.GetGuid(reader.GetOrdinal(columnName));

    public static Guid? GetNullableGuid(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    public static int GetInt32(this NpgsqlDataReader reader, string columnName)
        => reader.GetInt32(reader.GetOrdinal(columnName));

    public static int? GetNullableInt32(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static bool GetBoolean(this NpgsqlDataReader reader, string columnName)
        => reader.GetBoolean(reader.GetOrdinal(columnName));

    public static DateTime GetDateTime(this NpgsqlDataReader reader, string columnName)
        => reader.GetDateTime(reader.GetOrdinal(columnName));

    public static DateTime? GetNullableDateTime(this NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
