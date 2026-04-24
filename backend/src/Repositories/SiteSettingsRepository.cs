using Api.Services;

namespace Api.Repositories;

/// <summary>
/// Repository for site-level settings stored in the control_plane database.
/// These apply platform-wide (rate limits, brute-force thresholds, upload constraints).
/// </summary>
public interface ISiteSettingsRepository
{
    /// <summary>Load all site-level setting overrides.</summary>
    Task<Dictionary<string, string>> GetAllAsync();

    /// <summary>Upsert a single site-level setting.</summary>
    Task UpsertAsync(string key, string value, string category);

    /// <summary>Delete a site-level setting override, reverting to the compiled default.</summary>
    Task<bool> DeleteAsync(string key);
}

public class SiteSettingsRepository : ISiteSettingsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SiteSettingsRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = SiteSettingsCommandFactory.CreateSelectAllSettingsCommand(conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await SiteSettingsReaderFlow.ReadAllSettingsAsync(reader);
    }

    public async Task UpsertAsync(string key, string value, string category)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = SiteSettingsCommandFactory.CreateUpsertSettingCommand(conn, key, value, category);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> DeleteAsync(string key)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = SiteSettingsCommandFactory.CreateDeleteSettingCommand(conn, key);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
