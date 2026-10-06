using Api.Constants;
using Api.Helpers;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

/// <summary>
/// The statements behind a user erasure. Control-plane scoped on purpose: the purge is the one
/// place that must reach every tenant database a person belonged to, so it resolves those
/// databases from the control plane and opens each one by identifier. Orchestration and
/// ordering live in <see cref="UserDataPurger"/>.
/// </summary>
public sealed class UserPurgeRepository
{
    /// <summary>
    /// Per-user rows in a tenant database without a foreign key to <c>users</c>, then the mirror
    /// row itself, whose cascades remove preferences, request templates and site memberships and
    /// whose set-nulls detach feedback, people and audit actors.
    /// <c>ai_daily_usage.subject</c> holds the user id as text (see <c>AiAccessService.SubjectFor</c>).
    /// </summary>
    internal static readonly string[] TenantPurgeStatements =
    [
        "DELETE FROM calendar_feed_tokens WHERE user_id = @id",
        "DELETE FROM ai_conversations WHERE user_id = @id",
        "DELETE FROM ai_usage WHERE user_id = @id",
        "DELETE FROM ai_daily_usage WHERE subject = @idText",
        "DELETE FROM ai_user_allowances WHERE user_id = @id",
        "DELETE FROM users WHERE id = @id",
    ];

    private readonly IDbConnectionFactory _connectionFactory;

    public UserPurgeRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// Identifiers of every tenant database the user has a membership in. Every membership
    /// status counts: a suspended or deleting tenant still has its database until the tenant
    /// purge drops it, and the person's rows in it must go with them.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListTenantDatabasesAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT t.db_identifier
            FROM tenant_memberships m
            JOIN tenants t ON t.id = m.tenant_id
            WHERE m.user_id = @id
            ORDER BY 1", conn);
        cmd.Parameters.AddWithValue("id", userId);

        var databases = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            databases.Add(reader.GetString("db_identifier"));
        return databases;
    }

    /// <summary>Names of the organizations the user owns, excluding ones already being deleted.</summary>
    public async Task<List<string>> ListOwnedTenantNamesAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(
            "SELECT display_name FROM tenants WHERE owner_user_id = @id AND status <> 'deleting' ORDER BY display_name",
            p => p.AddWithValue("id", userId), r => r.GetString("display_name"), ct);
    }

    /// <summary>
    /// Names of the organizations where the user is the only active admin: the read-only form of
    /// <see cref="ActiveAdminGuard"/>'s rule, asked before an erasure rather than enforced by it.
    /// </summary>
    public async Task<List<string>> ListTenantNamesWhereLastActiveAdminAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT t.display_name
            FROM tenant_memberships m JOIN tenants t ON t.id = m.tenant_id
            WHERE m.user_id = @id AND m.role = @adminRole AND m.status = @active AND t.status <> 'deleting'
              AND NOT EXISTS (
                  SELECT 1 FROM tenant_memberships o
                  WHERE o.tenant_id = m.tenant_id AND o.user_id <> m.user_id
                    AND o.role = @adminRole AND o.status = @active)
            ORDER BY t.display_name",
            p =>
            {
                p.AddWithValue("id", userId);
                p.AddWithValue("adminRole", RoleConstants.Admin);
                p.AddWithValue("active", MembershipStatusConstants.Active);
            }, r => r.GetString("display_name"), ct);
    }

    /// <summary>Removes the user's rows from one tenant database, in one transaction.</summary>
    public async Task PurgeTenantDatabaseAsync(string dbIdentifier, Guid userId, CancellationToken ct = default)
    {
        await using var tenantDb = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
        await tenantDb.OpenAsync(ct);
        await using var tx = await tenantDb.BeginTransactionAsync(ct);

        foreach (var sql in TenantPurgeStatements)
        {
            await using var cmd = new NpgsqlCommand(sql, tenantDb, tx);
            cmd.Parameters.AddWithValue("id", userId);
            cmd.Parameters.AddWithValue("idText", userId.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Deletes the control-plane row. Its cascades remove identities, sessions, ToS acceptances,
    /// tenant memberships and sent invitations.
    /// </summary>
    public async Task DeleteControlPlaneUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
