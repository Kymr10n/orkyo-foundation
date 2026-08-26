using Npgsql;

namespace Api.Repositories;

/// <summary>
/// The shared body of the two settings repositories. <c>tenant_settings</c> (in the
/// workspace's own database) and <c>site_settings</c> (in the control plane) are the same
/// key/value/category table reached through different connections, and their repositories
/// were byte-identical apart from the table name and the factory call.
///
/// They had already begun to rot in the same way: each file's <c>UpsertAsync</c> wrote
/// <c>value = @value</c> while its own <c>UpsertManyAsync</c> seventeen lines below wrote
/// <c>value = EXCLUDED.value</c>. One body means one idiom — EXCLUDED, which reads the
/// value being inserted rather than a parameter that happens to hold it.
///
/// The table name is interpolated because a table cannot be parameterized; it is a
/// compile-time constant supplied by the subclass, never anything a caller can influence.
/// </summary>
public abstract class KeyValueSettingsRepository
{
    /// <summary>The settings table this repository reads and writes.</summary>
    protected abstract string TableName { get; }

    /// <summary>Opens a connection to the database that holds <see cref="TableName"/>.</summary>
    protected abstract NpgsqlConnection CreateConnection();

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        var rows = await conn.QueryListAsync($"SELECT key, value FROM {TableName}", null,
            r => (r.GetString(0), r.GetString(1)), ct);
        var settings = new Dictionary<string, string>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in rows) settings[k] = v;
        return settings;
    }

    public async Task UpsertAsync(string key, string value, string category, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.ExecuteAsync($@"
            INSERT INTO {TableName} (key, value, category, updated_at)
            VALUES (@key, @value, @category, NOW())
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = NOW()",
            p =>
            {
                p.AddWithValue("key", key);
                p.AddWithValue("value", value);
                p.AddWithValue("category", category);
            }, ct);
    }

    public async Task UpsertManyAsync(IReadOnlyCollection<(string Key, string Value, string Category)> settings, CancellationToken ct = default)
    {
        if (settings.Count == 0) return;
        await using var conn = CreateConnection();
        await conn.ExecuteAsync($@"
            INSERT INTO {TableName} (key, value, category, updated_at)
            SELECT k, v, c, NOW()
            FROM UNNEST(@keys::text[], @values::text[], @categories::text[]) AS t(k, v, c)
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = NOW()",
            p =>
            {
                p.AddWithValue("keys", settings.Select(s => s.Key).ToArray());
                p.AddWithValue("values", settings.Select(s => s.Value).ToArray());
                p.AddWithValue("categories", settings.Select(s => s.Category).ToArray());
            }, ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        return await conn.ExecuteAsync($"DELETE FROM {TableName} WHERE key = @key",
            p => p.AddWithValue("key", key), ct) > 0;
    }
}
