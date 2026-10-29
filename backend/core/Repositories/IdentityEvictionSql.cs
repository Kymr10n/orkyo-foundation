using Api.Helpers;
using Npgsql;

namespace Api.Repositories;

/// <summary>
/// What a status write needs to evict a user's identity cache entries
/// (<see cref="Api.Services.Caching.IdentityCacheEvictor"/>), on a control-plane connection the
/// caller already holds.
/// </summary>
internal static class IdentityEvictionSql
{
    /// <summary>The tenants a user has a membership row in, any status.</summary>
    internal static Task<List<Guid>> TenantIdsOfUserAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct) =>
        conn.QueryListAsync(
            "SELECT tenant_id FROM tenant_memberships WHERE user_id = @userId",
            p => p.AddWithValue("userId", userId),
            reader => reader.GetGuid("tenant_id"),
            ct);

    /// <summary>The external (Keycloak) subjects linked to a user: the principal cache's keys.</summary>
    internal static Task<List<string>> ExternalSubjectsOfUserAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct) =>
        conn.QueryListAsync(
            "SELECT provider_subject FROM user_identities WHERE user_id = @userId",
            p => p.AddWithValue("userId", userId),
            reader => reader.GetString("provider_subject"),
            ct);
}
