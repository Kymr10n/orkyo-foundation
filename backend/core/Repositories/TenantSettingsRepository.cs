using Api.Services;

namespace Api.Repositories;

public interface ITenantSettingsRepository
{
    /// <summary>Load all setting overrides for the current tenant.</summary>
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Upsert a single setting.</summary>
    Task UpsertAsync(string key, string value, string category, CancellationToken ct = default);

    /// <summary>Delete a setting override, reverting to the compiled default.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}

public class TenantSettingsRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : ITenantSettingsRepository
{
    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var rows = await conn.QueryListAsync("SELECT key, value FROM tenant_settings", null,
            r => (r.GetString(0), r.GetString(1)), ct);
        var settings = new Dictionary<string, string>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in rows) settings[k] = v;
        return settings;
    }

    public async Task UpsertAsync(string key, string value, string category, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        await conn.ExecuteAsync(@"
            INSERT INTO tenant_settings (key, value, category, updated_at)
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
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync("DELETE FROM tenant_settings WHERE key = @key",
            p => p.AddWithValue("key", key), ct) > 0;
    }
}
