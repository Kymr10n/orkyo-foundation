using Npgsql;

namespace Api.Services;

/// <summary>
/// Erases one user's application data everywhere it lives. Used by the GDPR lifecycle purge
/// (<see cref="UserLifecycleService"/>) and the site-admin permanent delete
/// (<see cref="UserManagementService"/>), so both paths remove the same rows.
///
/// A user's data is spread over two layers:
/// <list type="bullet">
///   <item><description>Every tenant database the user is a member of holds a <c>users</c> mirror
///   row plus per-user rows that carry no foreign key to it: calendar feed tokens, assistant
///   conversations and allowances, token and daily usage. Rows that do reference the mirror
///   cascade (preferences, request templates, site memberships) or null out (feedback, people,
///   audit actor) when the mirror goes.</description></item>
///   <item><description>The control-plane <c>users</c> row, whose cascades remove identities,
///   sessions, ToS acceptances, tenant memberships and sent invitations.</description></item>
/// </list>
///
/// Tenant databases are purged first and the control-plane row last, inside its own
/// transaction. A tenant database that cannot be reached throws, so the control-plane row
/// stays, the user remains in the purge queue, and the next run retries. Deleting the
/// control-plane row first would also delete the memberships that say which tenant databases
/// still hold the person's data.
///
/// Community runs control plane and tenant in one database and its
/// <see cref="IDbConnectionFactory"/> maps every identifier to it; there the tenant step deletes
/// the only <c>users</c> row and the control-plane step is a no-op.
/// </summary>
public sealed class UserDataPurger
{
    /// <summary>
    /// Per-user rows in a tenant database without a foreign key to <c>users</c>, then the mirror
    /// row itself. Order matters only for readability; nothing here references anything else.
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
    private readonly ILogger _logger;

    public UserDataPurger(IDbConnectionFactory connectionFactory, ILogger logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Removes the user from every tenant database they belong to, then from the control plane.
    /// Throws when a tenant database cannot be purged; the control-plane row is then left in place.
    /// </summary>
    public async Task PurgeAsync(Guid userId, CancellationToken ct = default)
    {
        await using var controlPlane = _connectionFactory.CreateControlPlaneConnection();
        await controlPlane.OpenAsync(ct);

        var tenantDatabases = await ListTenantDatabasesAsync(controlPlane, userId, ct);
        foreach (var dbIdentifier in tenantDatabases)
        {
            ct.ThrowIfCancellationRequested();
            await PurgeTenantDatabaseAsync(dbIdentifier, userId, ct);
        }

        await using var tx = await controlPlane.BeginTransactionAsync(ct);
        await using var deleteCmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id", controlPlane, tx);
        deleteCmd.Parameters.AddWithValue("id", userId);
        await deleteCmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Purged user {UserId} from {TenantCount} tenant database(s) and the control plane",
            userId, tenantDatabases.Count);
    }

    private static async Task<List<string>> ListTenantDatabasesAsync(
        NpgsqlConnection controlPlane, Guid userId, CancellationToken ct)
    {
        // Every membership status counts: a suspended or deleting tenant still has its database
        // until the tenant purge drops it, and the person's rows in it must go with them.
        await using var cmd = new NpgsqlCommand(@"
            SELECT DISTINCT t.db_identifier
            FROM tenant_memberships m
            JOIN tenants t ON t.id = m.tenant_id
            WHERE m.user_id = @id
            ORDER BY 1", controlPlane);
        cmd.Parameters.AddWithValue("id", userId);

        var databases = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            databases.Add(reader.GetString(0));
        return databases;
    }

    private async Task PurgeTenantDatabaseAsync(string dbIdentifier, Guid userId, CancellationToken ct)
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
        _logger.LogInformation("Purged user {UserId} from tenant database {Database}", userId, dbIdentifier);
    }
}
