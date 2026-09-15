using Api.Helpers;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface ITenantSettingsRepository
{
    /// <summary>Load all setting overrides for the current tenant.</summary>
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Upsert a single setting.</summary>
    Task UpsertAsync(string key, string value, string category, CancellationToken ct = default);

    /// <summary>Upsert many settings in a single statement/connection (atomic — no half-applied config).</summary>
    Task UpsertManyAsync(IReadOnlyCollection<(string Key, string Value, string Category)> settings, CancellationToken ct = default);

    /// <summary>Delete a setting override, reverting to the compiled default.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
}

/// <remarks>
/// Takes the nullable accessor, not <see cref="OrgContext"/>: <see cref="TenantSettingsService"/>
/// depends on this repository and must construct in a scope with no tenant (it serves site scope
/// there). The typed failure moves from construction to the first tenant query.
/// </remarks>
public class TenantSettingsRepository(IOrgContextAccessor orgContextAccessor, IOrgDbConnectionFactory connectionFactory)
    : KeyValueSettingsRepository, ITenantSettingsRepository
{
    protected override string TableName => "tenant_settings";

    protected override NpgsqlConnection CreateConnection() =>
        connectionFactory.CreateOrgConnection(orgContextAccessor.Current ?? throw new TenantContextUnavailableException());
}
