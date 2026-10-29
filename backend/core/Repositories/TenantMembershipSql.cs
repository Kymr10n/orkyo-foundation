using Api.Helpers;
using Npgsql;

namespace Api.Repositories;

/// <summary>Membership reads that callers run on a control-plane connection they already hold.</summary>
internal static class TenantMembershipSql
{
    /// <summary>The tenants a user has a membership row in, any status.</summary>
    internal static Task<List<Guid>> TenantIdsOfUserAsync(NpgsqlConnection conn, Guid userId, CancellationToken ct) =>
        conn.QueryListAsync(
            "SELECT tenant_id FROM tenant_memberships WHERE user_id = @userId",
            p => p.AddWithValue("userId", userId),
            reader => reader.GetGuid("tenant_id"),
            ct);
}
