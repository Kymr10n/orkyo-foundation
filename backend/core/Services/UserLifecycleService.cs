using Api.Constants;
using Api.Integrations.Keycloak;
using Microsoft.Extensions.DependencyInjection;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// GDPR user account lifecycle — product-agnostic, consumed by both SaaS and Community workers.
///
/// Flow:
///   1. Users inactive 12+ months → warning email #1 with confirm-activity link.
///   2. No response in 14 days    → warning email #2.
///   3. No response in 14 days    → warning email #3 (final).
///   4. No response in 14 days    → account disabled in Keycloak (dormant), dormancy notice sent.
///   5. Dormant 90+ days          → purged from Keycloak and app data deleted.
///
/// State is stored in the <c>users</c> table (lifecycle_* columns).
/// Reset occurs on login (<c>/api/session/bootstrap</c>) or confirm-activity link.
///
/// The DB connection is created via <see cref="IDbConnectionFactory.CreateControlPlaneConnection"/>.
/// In Community, the factory maps this to the single deployment database.
///
/// Keycloak calls go through <see cref="IKeycloakAdminService"/> (which sets the
/// X-Forwarded-Proto/Host headers the internal proxy policy requires) and emails through
/// <see cref="IEmailService"/> — both resolved from a fresh scope per run, because the workers
/// host this service as a singleton while those services may be typed-HttpClient/scoped.
/// </summary>
public sealed class UserLifecycleService
{
    private readonly ILogger<UserLifecycleService> _logger;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IServiceScopeFactory _scopeFactory;

    public UserLifecycleService(
        ILogger<UserLifecycleService> logger,
        IDbConnectionFactory connectionFactory,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _connectionFactory = connectionFactory;
        _scopeFactory = scopeFactory;
    }

    public async Task ProcessAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting user lifecycle run");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var keycloakAdmin = scope.ServiceProvider.GetRequiredService<IKeycloakAdminService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await SendWarningsAsync(db, emailService, 0, ct);
        await SendWarningsAsync(db, emailService, 1, ct);
        await SendWarningsAsync(db, emailService, 2, ct);
        await DeactivatePersistentlyInactiveUsersAsync(db, keycloakAdmin, emailService, ct);
        await PurgeDormantUsersAsync(db, keycloakAdmin, ct);

        _logger.LogInformation("User lifecycle run complete");
    }

    private async Task SendWarningsAsync(
        Npgsql.NpgsqlConnection db, IEmailService emailService, int currentWarningCount, CancellationToken ct)
    {
        var nextCount = currentWarningCount + 1;

        var cmd = currentWarningCount == 0
            ? new Npgsql.NpgsqlCommand($@"
                SELECT id, email, display_name, keycloak_id
                FROM users
                WHERE lifecycle_status IS NULL
                  AND status = 'active'
                  AND (
                    (last_login_at IS NOT NULL AND last_login_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserInactiveWarningSqlInterval}')
                    OR
                    (last_login_at IS NULL AND created_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserInactiveWarningSqlInterval}')
                  )", db)
            : new Npgsql.NpgsqlCommand($@"
                SELECT id, email, display_name, keycloak_id
                FROM users
                WHERE lifecycle_status = 'warned'
                  AND lifecycle_warning_count = @count
                  AND lifecycle_last_warned_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserWarningReminderSqlInterval}'", db);

        if (currentWarningCount > 0)
            cmd.Parameters.AddWithValue("count", currentWarningCount);

        var users = await ReadUsersAsync(cmd, ct);
        _logger.LogInformation("Warning phase #{NextCount}: {Count} user(s) to warn", nextCount, users.Count);

        foreach (var user in users)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var token = Guid.NewGuid().ToString();

                await using var tx = await db.BeginTransactionAsync(ct);
                await UpdateLifecycleAsync(db, user.Id, status: "warned", warningCount: nextCount,
                    lastWarnedAt: DateTime.UtcNow, dormantSince: null, confirmToken: token, ct);
                await tx.CommitAsync(ct);

                await emailService.SendLifecycleWarningEmailAsync(user.Email, user.DisplayName, token, warningNumber: nextCount, ct);
                _logger.LogInformation("Warning #{NextCount} sent to user {UserId}", nextCount, user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process warning #{NextCount} for user {UserId}", nextCount, user.Id);
            }
        }
    }

    private async Task DeactivatePersistentlyInactiveUsersAsync(
        Npgsql.NpgsqlConnection db, IKeycloakAdminService keycloakAdmin, IEmailService emailService, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand($@"
            SELECT id, email, display_name, keycloak_id
            FROM users
            WHERE lifecycle_status = 'warned'
              AND lifecycle_warning_count = 3
              AND lifecycle_last_warned_at < NOW() - INTERVAL '{LifecyclePolicyConstants.UserWarningReminderSqlInterval}'
        ", db);

        var users = await ReadUsersAsync(cmd, ct);
        _logger.LogInformation("Phase 4 (deactivate): {Count} user(s) to deactivate", users.Count);

        foreach (var user in users)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!string.IsNullOrEmpty(user.KeycloakId))
                {
                    try
                    {
                        await keycloakAdmin.DisableUserAsync(user.KeycloakId, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to disable Keycloak user {KeycloakId}", user.KeycloakId);
                        continue;
                    }
                }

                await using var tx = await db.BeginTransactionAsync(ct);
                await UpdateLifecycleAsync(db, user.Id, status: "dormant", warningCount: 3,
                    lastWarnedAt: null, dormantSince: DateTime.UtcNow, confirmToken: null, ct);
                await SetUserDbStatusAsync(db, user.Id, UserStatusConstants.Disabled, ct);
                await tx.CommitAsync(ct);

                await emailService.SendDormancyNoticeEmailAsync(user.Email, user.DisplayName, ct);
                _logger.LogWarning("User {UserId} deactivated — no response to 3 lifecycle warnings", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deactivate user {UserId}", user.Id);
            }
        }
    }

    private async Task PurgeDormantUsersAsync(
        Npgsql.NpgsqlConnection db, IKeycloakAdminService keycloakAdmin, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand($@"
            SELECT id, email, display_name, keycloak_id
            FROM users
            WHERE lifecycle_status = 'dormant'
              AND lifecycle_dormant_since < NOW() - INTERVAL '{LifecyclePolicyConstants.UserPurgeAfterDormantSqlInterval}'
        ", db);

        var users = await ReadUsersAsync(cmd, ct);
        _logger.LogInformation("Phase 5 (purge): {Count} user(s) to purge", users.Count);

        foreach (var user in users)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (!string.IsNullOrEmpty(user.KeycloakId))
                {
                    try
                    {
                        await keycloakAdmin.DeleteUserAsync(user.KeycloakId, ct);
                    }
                    catch (Exception ex)
                    {
                        // Log and proceed — the GDPR purge of app data must not be blocked
                        // by a Keycloak failure (pre-existing behavior).
                        _logger.LogError(ex, "Failed to delete Keycloak user {KeycloakId}", user.KeycloakId);
                    }
                }

                await using var tx = await db.BeginTransactionAsync(ct);
                var deleteCmd = new Npgsql.NpgsqlCommand("DELETE FROM users WHERE id = @id", db);
                deleteCmd.Parameters.AddWithValue("id", user.Id);
                await deleteCmd.ExecuteNonQueryAsync(ct);
                await tx.CommitAsync(ct);

                _logger.LogWarning("GDPR purge: user {UserId} ({Email}) permanently deleted", user.Id, user.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge user {UserId}", user.Id);
            }
        }
    }

    private static async Task<List<(Guid Id, string Email, string DisplayName, string? KeycloakId)>> ReadUsersAsync(
        Npgsql.NpgsqlCommand cmd, CancellationToken ct)
    {
        var results = new List<(Guid, string, string, string?)>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        return results;
    }

    private static async Task UpdateLifecycleAsync(
        Npgsql.NpgsqlConnection db, Guid userId, string? status, int warningCount,
        DateTime? lastWarnedAt, DateTime? dormantSince, string? confirmToken, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand($@"
            UPDATE users
            SET lifecycle_status = @status,
                lifecycle_warning_count = @warningCount,
                lifecycle_last_warned_at = @lastWarnedAt,
                lifecycle_dormant_since = @dormantSince,
                lifecycle_confirm_token = @confirmToken,
                lifecycle_confirm_token_expires_at = CASE
                    WHEN @confirmToken IS NULL THEN NULL
                    ELSE NOW() + INTERVAL '{LifecyclePolicyConstants.UserConfirmTokenValiditySqlInterval}'
                END,
                updated_at = NOW()
            WHERE id = @id", db);
        cmd.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("warningCount", warningCount);
        cmd.Parameters.AddWithValue("lastWarnedAt", (object?)lastWarnedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("dormantSince", (object?)dormantSince ?? DBNull.Value);
        // Explicitly typed: @confirmToken appears in "CASE WHEN @confirmToken IS NULL", where an
        // untyped NULL makes Postgres fail parameter-type inference (42P08) — which silently broke
        // the deactivate phase (the only caller passing a null token) until the service tests caught it.
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter("confirmToken", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = (object?)confirmToken ?? DBNull.Value
        });
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SetUserDbStatusAsync(Npgsql.NpgsqlConnection db, Guid userId, string status, CancellationToken ct)
    {
        var cmd = new Npgsql.NpgsqlCommand("UPDATE users SET status = @status, updated_at = NOW() WHERE id = @id", db);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
