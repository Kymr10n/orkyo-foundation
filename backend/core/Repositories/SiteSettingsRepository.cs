using Api.Services;

namespace Api.Repositories;

/// <summary>
/// Repository for site-level settings stored in the control_plane database.
/// These apply platform-wide (rate limits, brute-force thresholds, upload constraints).
/// </summary>
public interface ISiteSettingsRepository
{
    /// <summary>Load all site-level setting overrides.</summary>
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Upsert a single site-level setting.</summary>
    Task UpsertAsync(string key, string value, string category, CancellationToken ct = default);

    /// <summary>Delete a site-level setting override, reverting to the compiled default.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}

public class SiteSettingsRepository(IDbConnectionFactory connectionFactory) : ISiteSettingsRepository
{
    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        var rows = await conn.QueryListAsync("SELECT key, value FROM site_settings", null,
            r => (r.GetString(0), r.GetString(1)), ct);
        var settings = new Dictionary<string, string>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in rows) settings[k] = v;
        return settings;
    }

    public async Task UpsertAsync(string key, string value, string category, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            INSERT INTO site_settings (key, value, category, updated_at)
            VALUES (@key, @value, @category, NOW())
            ON CONFLICT (key) DO UPDATE SET value = @value, updated_at = NOW()",
            p =>
            {
                p.AddWithValue("key", key);
                p.AddWithValue("value", value);
                p.AddWithValue("category", category);
            }, ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        return await conn.ExecuteAsync("DELETE FROM site_settings WHERE key = @key",
            p => p.AddWithValue("key", key), ct) > 0;
    }
}
