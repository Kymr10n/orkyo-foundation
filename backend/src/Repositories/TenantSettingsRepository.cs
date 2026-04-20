using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface ITenantSettingsRepository
{
    /// <summary>Load all setting overrides for the current tenant.</summary>
    Task<Dictionary<string, string>> GetAllAsync();

    /// <summary>Upsert a single setting.</summary>
    Task UpsertAsync(string key, string value, string category);

    /// <summary>Delete a setting override, reverting to the compiled default.</summary>
    Task<bool> DeleteAsync(string key);
}

public class TenantSettingsRepository : ITenantSettingsRepository
{
    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public TenantSettingsRepository(
        OrgContext orgContext,
        IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT key, value FROM tenant_settings", conn);

        await using var reader = await cmd.ExecuteReaderAsync();
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync())
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return settings;
    }

    public async Task UpsertAsync(string key, string value, string category)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO tenant_settings (key, value, category, updated_at)
            VALUES (@key, @value, @category, NOW())
            ON CONFLICT (key) DO UPDATE SET
                value = @value,
                updated_at = NOW()", conn);

        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.Parameters.AddWithValue("category", category);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteAsync(string key)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM tenant_settings WHERE key = @key", conn);
        cmd.Parameters.AddWithValue("key", key);

        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
