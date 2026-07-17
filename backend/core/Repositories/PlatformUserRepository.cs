using Api.Models.Admin;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public class PlatformUserRepository : IPlatformUserRepository
{
    private const string PgUniqueViolation = "23505";

    private readonly IDbConnectionFactory _connectionFactory;

    public PlatformUserRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.ExistsAsync("users", userId, ct);
    }

    public async Task<List<AdminUserListRow>> GetAdminUserListAsync(string? search, string? status, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        var whereClauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClauses.Add("(LOWER(email) LIKE @search OR LOWER(display_name) LIKE @search)");
            parameters.Add(new NpgsqlParameter("search", $"%{search.ToLower()}%"));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            whereClauses.Add("status = @status");
            parameters.Add(new NpgsqlParameter("status", status));
        }

        var whereClause = whereClauses.Count > 0 ? $"WHERE {string.Join(" AND ", whereClauses)}" : "";

        var sql = $@"
            SELECT
                u.id, u.email, u.display_name, u.status, u.created_at, u.updated_at, u.last_login_at,
                (SELECT COUNT(*) FROM tenant_memberships tm WHERE tm.user_id = u.id AND tm.status = 'active') as membership_count,
                (SELECT COUNT(*) FROM user_identities ui WHERE ui.user_id = u.id) as identity_count,
                (SELECT ui.provider_subject FROM user_identities ui WHERE ui.user_id = u.id AND ui.provider = 'keycloak' LIMIT 1) as keycloak_sub,
                ot.id as owned_tenant_id
            FROM users u
            LEFT JOIN tenants ot ON ot.owner_user_id = u.id AND ot.status != 'deleting'
            {whereClause}
            ORDER BY u.email
            LIMIT 500";

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var param in parameters)
            cmd.Parameters.Add(param);

        var rows = new List<AdminUserListRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var keycloakSub = reader.IsDBNull(9) ? null : reader.GetString(9);
            var summary = new AdminUserSummary
            {
                Id = reader.GetGuid(0),
                Email = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
                Status = reader.GetString(3),
                CreatedAt = reader.GetDateTime(4),
                UpdatedAt = reader.GetDateTime(5),
                LastLoginAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                MembershipCount = reader.GetInt32(7),
                IdentityCount = reader.GetInt32(8),
                IsSiteAdmin = false, // resolved by the caller via Keycloak
                OwnedTenantId = reader.IsDBNull(10) ? null : reader.GetGuid(10),
                OwnedTenantTier = null, // resolved by the caller via the edition's plan provider
            };
            rows.Add(new AdminUserListRow(summary, keycloakSub));
        }
        return rows;
    }

    public async Task<AdminUserCoreDto?> GetAdminUserCoreAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(@"
            SELECT u.id, u.email, u.display_name, u.status, u.created_at, u.updated_at, u.last_login_at,
                   ot.id as owned_tenant_id
            FROM users u
            LEFT JOIN tenants ot ON ot.owner_user_id = u.id AND ot.status != 'deleting'
            WHERE u.id = @userId",
            p => p.AddWithValue("userId", userId),
            reader => new AdminUserCoreDto(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetGuid(7)),
            ct);
    }

    public async Task<(string Email, string DisplayName)?> GetEmailAndDisplayNameAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync<(string, string)?>(
            "SELECT email, display_name FROM users WHERE id = @id",
            p => p.AddWithValue("id", userId),
            reader =>
            {
                var email = reader.GetString(0);
                var displayName = reader.IsDBNull(1) ? email : reader.GetString(1);
                return (email, displayName);
            },
            ct);
    }

    public async Task<bool> SetPendingEmailChangeAsync(Guid userId, string newEmail, string token, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        try
        {
            await conn.ExecuteAsync(@"
                UPDATE users
                SET pending_email             = @pending,
                    email_change_token        = @token,
                    email_change_requested_at = NOW(),
                    updated_at                = NOW()
                WHERE id = @id",
                p =>
                {
                    p.AddWithValue("pending", newEmail);
                    p.AddWithValue("token", token);
                    p.AddWithValue("id", userId);
                }, ct);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PgUniqueViolation)
        {
            return false;
        }
    }

    public async Task ClearPendingEmailChangeAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            UPDATE users
            SET pending_email             = NULL,
                email_change_token        = NULL,
                email_change_requested_at = NULL,
                updated_at                = NOW()
            WHERE id = @id",
            p => p.AddWithValue("id", userId), ct);
    }

    public async Task<EmailChangeConfirmResult> ConfirmEmailChangeAsync(
        string token,
        Func<string?, string, string, CancellationToken, Task> updateKeycloakEmailAsync,
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        string? keycloakId;
        Guid userId;
        string currentEmail;
        string? pendingEmail;
        await using (var findCmd = new NpgsqlCommand(@"
            SELECT u.id,
                   COALESCE(
                       u.keycloak_id,
                       (
                           SELECT ui.provider_subject
                           FROM user_identities ui
                           WHERE ui.user_id = u.id
                             AND ui.provider = 'keycloak'
                           LIMIT 1
                       )
                   ) AS keycloak_id,
                   u.email,
                   u.pending_email
            FROM users u
            WHERE u.email_change_token = @token
              AND u.email_change_requested_at > NOW() - INTERVAL '24 hours'
            FOR UPDATE", conn, tx))
        {
            findCmd.Parameters.AddWithValue("token", token);
            await using var reader = await findCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return new EmailChangeConfirmResult(EmailChangeConfirmStatus.NotFoundOrExpired);

            userId = reader.GetGuid(0);
            keycloakId = reader.IsDBNull(1) ? null : reader.GetString(1);
            currentEmail = reader.GetString(2);
            pendingEmail = reader.IsDBNull(3) ? null : reader.GetString(3);
        }

        if (string.IsNullOrEmpty(pendingEmail))
            return new EmailChangeConfirmResult(EmailChangeConfirmStatus.MissingPendingEmail, userId);

        // Conflict pre-check: avoid calling Keycloak when the local UNIQUE (email) constraint
        // would reject the commit anyway. Without this, Keycloak would be updated and our DB
        // UPDATE would then fail with 23505, leaving the two stores divergent.
        await using (var conflictCmd = new NpgsqlCommand(@"
            SELECT EXISTS (
                SELECT 1 FROM users
                WHERE id <> @id AND lower(email) = lower(@pending)
            )", conn, tx))
        {
            conflictCmd.Parameters.AddWithValue("id", userId);
            conflictCmd.Parameters.AddWithValue("pending", pendingEmail);
            if ((bool)(await conflictCmd.ExecuteScalarAsync(ct))!)
                return new EmailChangeConfirmResult(EmailChangeConfirmStatus.Conflict, userId);
        }

        // Atomic idempotency: a concurrent confirmation or a stale link returns 0 rows.
        // UNIQUE (email) on users may raise 23505 if another user already has this address.
        int updated;
        try
        {
            await using var commitCmd = new NpgsqlCommand(@"
                UPDATE users
                SET email                      = pending_email,
                    pending_email              = NULL,
                    email_change_token         = NULL,
                    email_change_requested_at  = NULL,
                    updated_at                 = NOW()
                WHERE id = @id
                  AND email_change_token = @token", conn, tx);
            commitCmd.Parameters.AddWithValue("id", userId);
            commitCmd.Parameters.AddWithValue("token", token);
            updated = await commitCmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PgUniqueViolation)
        {
            return new EmailChangeConfirmResult(EmailChangeConfirmStatus.Conflict, userId);
        }
        if (updated != 1)
            return new EmailChangeConfirmResult(EmailChangeConfirmStatus.NotFoundOrExpired, userId);

        await using (var identityCmd = new NpgsqlCommand(@"
            UPDATE user_identities
            SET provider_email = @email
            WHERE user_id = @id
              AND provider = 'keycloak'", conn, tx))
        {
            identityCmd.Parameters.AddWithValue("id", userId);
            identityCmd.Parameters.AddWithValue("email", pendingEmail);
            await identityCmd.ExecuteNonQueryAsync(ct);
        }

        // The DB update has passed local constraints inside the transaction.
        // If Keycloak fails, rollback keeps the old email authoritative and
        // preserves the pending token so the user can retry.
        await updateKeycloakEmailAsync(keycloakId, currentEmail, pendingEmail, ct);
        await tx.CommitAsync(ct);

        return new EmailChangeConfirmResult(EmailChangeConfirmStatus.Confirmed, userId, pendingEmail);
    }

    public async Task<AccountLifecycleConfirmRecord?> FindActiveLifecycleConfirmAsync(string token, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync(@"
            SELECT id, keycloak_id, display_name, lifecycle_status
            FROM users
            WHERE lifecycle_confirm_token = @token
              AND lifecycle_status IS NOT NULL
              AND lifecycle_confirm_token_expires_at > NOW()",
            p => p.AddWithValue("token", token),
            reader =>
            {
                var userId = reader.GetGuid(0);
                var keycloakId = reader.IsDBNull(1) ? null : reader.GetString(1);
                var displayName = reader.GetString(2);
                var wasDormant = !reader.IsDBNull(3) && string.Equals(reader.GetString(3), "dormant", StringComparison.Ordinal);
                return new AccountLifecycleConfirmRecord(userId, keycloakId, displayName, wasDormant);
            },
            ct);
    }

    public async Task ClearLifecycleStateAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            UPDATE users
            SET lifecycle_status = NULL,
                lifecycle_warning_count = 0,
                lifecycle_last_warned_at = NULL,
                lifecycle_dormant_since = NULL,
                lifecycle_confirm_token = NULL,
                lifecycle_confirm_token_expires_at = NULL,
                updated_at = NOW()
            WHERE id = @id",
            p => p.AddWithValue("id", userId), ct);
    }

    public async Task<bool?> GetAnnouncementEmailOptOutAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QuerySingleOrDefaultAsync<bool?>(
            "SELECT announcement_email_opt_out FROM users WHERE id = @id",
            p => p.AddWithValue("id", userId),
            reader => reader.GetBoolean(0),
            ct);
    }

    public async Task SetAnnouncementEmailOptOutAsync(Guid userId, bool optOut, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        await conn.ExecuteAsync(@"
            UPDATE users SET announcement_email_opt_out = @v, updated_at = NOW()
            WHERE id = @id",
            p =>
            {
                p.AddWithValue("v", optOut);
                p.AddWithValue("id", userId);
            }, ct);
    }

    public async Task<bool> SetAnnouncementOptOutByTokenAsync(Guid unsubscribeToken, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        var rows = await conn.ExecuteAsync(@"
            UPDATE users
            SET announcement_email_opt_out = TRUE, updated_at = NOW()
            WHERE unsubscribe_token = @token",
            p => p.AddWithValue("token", unsubscribeToken), ct);
        return rows > 0;
    }

    public async Task<List<AdminUserMembership>> GetMembershipsAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryListAsync(@"
            SELECT tm.tenant_id, t.slug, t.display_name, tm.role, tm.status, tm.created_at
            FROM tenant_memberships tm
            INNER JOIN tenants t ON tm.tenant_id = t.id
            WHERE tm.user_id = @userId
            ORDER BY t.display_name",
            p => p.AddWithValue("userId", userId),
            reader => new AdminUserMembership
            {
                TenantId = reader.GetGuid(0),
                TenantSlug = reader.GetString(1),
                TenantName = reader.GetString(2),
                Role = reader.GetString(3),
                Status = reader.GetString(4),
                JoinedAt = reader.GetDateTime(5),
            },
            ct);
    }
}
