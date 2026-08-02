using System.Text.Json;
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

    // ── jsonb columns ────────────────────────────────────────────────────────
    //
    // JsonDocument.Parse rents its backing buffer from ArrayPool and owns it, so a
    // document that is never disposed never returns that buffer to the pool — pure
    // allocation churn on read paths that run per row. Clone() is what makes disposing
    // safe: it copies the element onto its own storage, detached from the document, so
    // the returned value outlives the `using`. Parsing without Clone() and without
    // dispose "works" only by accident, and pins the whole document behind one element.
    //
    // These live here so mappers cannot drift back to the hand-rolled form.

    /// <summary>Reads a Postgres text[] column. The view aggregates with COALESCE to an empty
    /// array, so this never sees NULL.</summary>
    public static IReadOnlyList<string> GetStringArray(this NpgsqlDataReader reader, string columnName)
        => reader.GetFieldValue<string[]>(reader.GetOrdinal(columnName));

    public static JsonElement GetJsonElement(this NpgsqlDataReader reader, string columnName)
        => reader.GetJsonElement(reader.GetOrdinal(columnName));

    public static JsonElement? GetNullableJsonElement(this NpgsqlDataReader reader, string columnName)
        => reader.GetNullableJsonElement(reader.GetOrdinal(columnName));

    /// <summary>
    /// Ordinal overload for mappers that read by position on purpose — a JOIN whose two
    /// tables share column names cannot be read by name (see <c>RequestMapper</c>).
    /// </summary>
    public static JsonElement GetJsonElement(this NpgsqlDataReader reader, int ordinal)
    {
        using var doc = JsonDocument.Parse(reader.GetString(ordinal));
        return doc.RootElement.Clone();
    }

    public static JsonElement? GetNullableJsonElement(this NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetJsonElement(ordinal);
}
