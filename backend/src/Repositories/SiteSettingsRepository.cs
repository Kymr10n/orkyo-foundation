using Api.Services;
using Npgsql;

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

public class SiteSettingsRepository : ISiteSettingsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SiteSettingsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("SELECT key, value FROM site_settings", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
            settings[reader.GetString(0)] = reader.GetString(1);
        return settings;
    }

    public async Task UpsertAsync(string key, string value, string category, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO site_settings (key, value, category, updated_at)
            VALUES (@key, @value, @category, NOW())
            ON CONFLICT (key) DO UPDATE SET
                value = @value,
                updated_at = NOW()", conn);
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.Parameters.AddWithValue("category", category);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand("DELETE FROM site_settings WHERE key = @key", conn);
        cmd.Parameters.AddWithValue("key", key);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }
}
