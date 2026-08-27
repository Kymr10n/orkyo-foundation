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

    /// <summary>Upsert many site-level settings in a single statement/connection (atomic).</summary>
    Task UpsertManyAsync(IReadOnlyCollection<(string Key, string Value, string Category)> settings, CancellationToken ct = default);

    /// <summary>Delete a site-level setting override, reverting to the compiled default.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}

public class SiteSettingsRepository(IDbConnectionFactory connectionFactory)
    : KeyValueSettingsRepository, ISiteSettingsRepository
{
    protected override string TableName => "site_settings";

    protected override NpgsqlConnection CreateConnection() =>
        connectionFactory.CreateControlPlaneConnection();
}
