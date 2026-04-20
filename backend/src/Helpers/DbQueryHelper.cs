using Api.Models;
using Npgsql;

namespace Api.Helpers;

/// <summary>
/// Helper methods for common database query patterns.
/// </summary>
public static class DbQueryHelper
{
    /// <summary>
    /// Allowlist of table names that may be passed to <see cref="ExistsAsync"/>.
    /// Prevents SQL injection via interpolated table name.
    /// </summary>
    private static readonly HashSet<string> AllowedTables = new(StringComparer.Ordinal)
    {
        "sites", "spaces", "space_groups", "criteria", "requests",
        "users", "tenants", "announcements", "presets", "templates"
    };

    /// <summary>
    /// Checks if a record exists in a table by ID.
    /// </summary>
    /// <param name="db">The database connection.</param>
    /// <param name="table">The table name (must be in the allow-list).</param>
    /// <param name="id">The record ID to check.</param>
    /// <returns>True if the record exists, false otherwise.</returns>
    /// <exception cref="ArgumentException">Thrown when table name is not in the allow-list.</exception>
    public static async Task<bool> ExistsAsync(NpgsqlConnection db, string table, Guid id)
    {
        if (!AllowedTables.Contains(table))
            throw new ArgumentException($"Table '{table}' is not in the allowed table list.", nameof(table));

        var cmd = new NpgsqlCommand($"SELECT 1 FROM {table} WHERE id = @id LIMIT 1", db);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteScalarAsync() != null;
    }

    /// <summary>
    /// Executes a command and returns the number of affected rows.
    /// </summary>
    /// <param name="db">The database connection.</param>
    /// <param name="query">The SQL command to execute.</param>
    /// <param name="id">The record ID parameter.</param>
    /// <returns>The number of rows affected.</returns>
    public static async Task<int> ExecuteDeleteAsync(NpgsqlConnection db, string query, Guid id)
    {
        var cmd = new NpgsqlCommand(query, db);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Executes a paginated query: runs a COUNT query first, then the paged SELECT,
    /// and returns a PagedResult wrapping the mapped items.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="conn">Open NpgsqlConnection to use for both commands.</param>
    /// <param name="page">PageRequest (sanitised internally).</param>
    /// <param name="countSql">SQL returning a single integer row count (no pagination params).</param>
    /// <param name="querySql">SQL returning rows; must include @limit and @offset parameters.</param>
    /// <param name="addParams">
    /// Optional callback to add filter parameters to the query command (e.g. WHERE clause params).
    /// Invoked after @limit and @offset are already added.
    /// For queries where the count SQL shares the same filter params, this callback is also applied
    /// to the count command unless <paramref name="addCountParams"/> is provided.
    /// </param>
    /// <param name="mapper">Function mapping a positioned NpgsqlDataReader row to T.</param>
    /// <param name="addCountParams">
    /// Optional callback to add filter parameters specifically to the count command.
    /// Defaults to <paramref name="addParams"/> when null.
    /// </param>
    public static async Task<PagedResult<T>> ExecutePagedQueryAsync<T>(
        NpgsqlConnection conn,
        PageRequest page,
        string countSql,
        string querySql,
        Action<NpgsqlCommand>? addParams,
        Func<NpgsqlDataReader, T> mapper,
        Action<NpgsqlCommand>? addCountParams = null)
    {
        var p = page.Sanitize();

        await using var countCmd = new NpgsqlCommand(countSql, conn);
        (addCountParams ?? addParams)?.Invoke(countCmd);
        var totalItems = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        await using var cmd = new NpgsqlCommand(querySql, conn);
        cmd.Parameters.AddWithValue("limit", p.PageSize);
        cmd.Parameters.AddWithValue("offset", p.Offset);
        addParams?.Invoke(cmd);

        var items = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            items.Add(mapper(reader));

        return PagedResult<T>.Create(items, totalItems, p);
    }
}
