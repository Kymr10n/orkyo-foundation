using System.ComponentModel.DataAnnotations;
using Api.Configuration;
using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orkyo.Shared;

namespace Api.Endpoints;


/// <summary>Minimal user projection for the account lifecycle confirm-activity flow.</summary>
public sealed record AccountLifecycleConfirmRecord(
    Guid UserId,
    string? KeycloakId,
    string DisplayName,
    bool WasDormant);

public static class AccountLifecycleEndpoints
{
    public static void MapAccountLifecycleEndpoints(this WebApplication app)
    {
        // GET /api/account/confirm-activity?token=<uuid>
        // Public endpoint — no auth required. User clicks this link from a lifecycle warning email.
        // Clears lifecycle state and redirects to the app. If the account was dormant, re-enables it in Keycloak.
        app.MapGet("/api/account/confirm-activity", [AllowAnonymous] async (
            string? token,
            IDbConnectionFactory dbFactory,
            IKeycloakAdminService keycloakAdmin,
            IConfiguration configuration,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            var appBaseUrl = configuration.GetRequired(ConfigKeys.AppBaseUrl);

            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("confirm-activity called without token");
                return Results.Redirect($"{appBaseUrl}?lifecycle=invalid");
            }

            try
            {
                await using var db = dbFactory.CreateControlPlaneConnection();
                await db.OpenAsync();

                // Find the user by confirm token
                AccountLifecycleConfirmRecord? record;
                await using (var findCmd = new Npgsql.NpgsqlCommand(@"
                    SELECT id, keycloak_id, display_name, lifecycle_status
                    FROM users
                    WHERE lifecycle_confirm_token = @token
                      AND lifecycle_status IS NOT NULL
                      AND lifecycle_confirm_token_expires_at > NOW()", db))
                {
                    findCmd.Parameters.AddWithValue("token", token);
                    await using var reader = await findCmd.ExecuteReaderAsync();
                    if (!await reader.ReadAsync()) { record = null; }
                    else
                    {
                        var userId = reader.GetGuid(0);
                        var keycloakId = reader.IsDBNull(1) ? null : reader.GetString(1);
                        var displayName = reader.GetString(2);
                        var wasDormant = !reader.IsDBNull(3) && string.Equals(reader.GetString(3), "dormant", StringComparison.Ordinal);
                        record = new AccountLifecycleConfirmRecord(userId, keycloakId, displayName, wasDormant);
                    }
                }

                if (record is null)
                {
                    logger.LogWarning("confirm-activity: token not found or already used: {TokenPrefix}",
                        token.Length > 8 ? token[..8] : "***");
                    return Results.Redirect($"{appBaseUrl}?lifecycle=expired");
                }

                // Re-enable in Keycloak if account was dormant
                if (record.WasDormant && !string.IsNullOrEmpty(record.KeycloakId))
                {
                    try
                    {
                        await keycloakAdmin.EnableUserAsync(record.KeycloakId);
                    }
                    catch (KeycloakAdminException ex)
                    {
                        logger.LogError(ex, "confirm-activity: failed to re-enable Keycloak user {KeycloakId}", record.KeycloakId);
                        // Continue clearing lifecycle state — user can still log in if Keycloak re-enable fails
                    }
                }

                // Clear lifecycle state
                await using var clearCmd = new Npgsql.NpgsqlCommand(@"
                    UPDATE users
                    SET lifecycle_status = NULL,
                        lifecycle_warning_count = 0,
                        lifecycle_last_warned_at = NULL,
                        lifecycle_dormant_since = NULL,
                        lifecycle_confirm_token = NULL,
                        lifecycle_confirm_token_expires_at = NULL,
                        updated_at = NOW()
                    WHERE id = @id", db);
                clearCmd.Parameters.AddWithValue("id", record.UserId);
                await clearCmd.ExecuteNonQueryAsync();

                logger.LogInformation(
                    "User {UserId} ({DisplayName}) confirmed activity — lifecycle state cleared (wasDormant={WasDormant})",
                    record.UserId, record.DisplayName, record.WasDormant);

                return Results.Redirect($"{appBaseUrl}?lifecycle=confirmed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing confirm-activity token");
                return Results.Redirect($"{appBaseUrl}?lifecycle=error");
            }
        })
        .WithName("ConfirmAccountActivity")
        .WithSummary("Confirm account activity from lifecycle warning email")
        .WithTags("Account")
        .WithMetadata(new SkipTenantResolutionAttribute());
    }
}

public sealed record RequestEmailChangeRequest(string NewEmail);

public static class AccountEmailChangeEndpoints
{
    private const string PgUniqueViolation = "23505";
    private static readonly EmailAddressAttribute EmailAddressValidator = new();

    public static void MapAccountEmailChangeEndpoints(this WebApplication app)
    {
        // POST /api/account/email — authenticated; stores a pending change and sends confirmation to the new address.
        app.MapPost("/api/account/email", async (
            RequestEmailChangeRequest request,
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IDbConnectionFactory dbFactory,
            IKeycloakAdminService keycloakAdmin,
            IEmailService emailService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            accountGuard.EnsureCanMutateOwnAccount(principal);
            var userId = principal.RequireUserId();
            if (string.IsNullOrWhiteSpace(request.NewEmail))
                return ErrorResponses.BadRequest("Email address is required.");

            var newEmail = request.NewEmail.Trim().ToLowerInvariant();
            if (!EmailAddressValidator.IsValid(newEmail))
                return ErrorResponses.BadRequest("Enter a valid email address.");

            await using var db = dbFactory.CreateControlPlaneConnection();
            await db.OpenAsync();

            string currentEmail;
            string displayName;
            await using (var selectCmd = new Npgsql.NpgsqlCommand(
                "SELECT email, display_name FROM users WHERE id = @id", db))
            {
                selectCmd.Parameters.AddWithValue("id", userId);
                await using var reader = await selectCmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return Results.NotFound();
                currentEmail = reader.GetString(0);
                displayName = reader.IsDBNull(1) ? currentEmail : reader.GetString(1);
            }

            if (string.Equals(currentEmail, newEmail, StringComparison.OrdinalIgnoreCase))
                return ErrorResponses.BadRequest("The new email address is the same as your current one.");

            // Reject if already taken in Keycloak — local DB uniqueness is enforced by the UNIQUE indexes below
            bool taken;
            try { taken = await keycloakAdmin.UserExistsAsync(newEmail, ct); }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to check email availability for {NewEmail}", newEmail);
                return Results.Problem("Could not verify email availability. Please try again.");
            }
            if (taken)
                return ErrorResponses.Conflict("That email address is already in use.");

            var token = Guid.NewGuid().ToString();

            try
            {
                await using var updateCmd = new Npgsql.NpgsqlCommand(@"
                    UPDATE users
                    SET pending_email             = @pending,
                        email_change_token        = @token,
                        email_change_requested_at = NOW(),
                        updated_at                = NOW()
                    WHERE id = @id", db);
                updateCmd.Parameters.AddWithValue("pending", newEmail);
                updateCmd.Parameters.AddWithValue("token", token);
                updateCmd.Parameters.AddWithValue("id", userId);
                await updateCmd.ExecuteNonQueryAsync(ct);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == PgUniqueViolation)
            {
                return ErrorResponses.Conflict("That email address is already in use.");
            }

            // IEmailService returns false on SMTP failure (no exception thrown).
            // If we cannot deliver the confirmation link the user can never complete
            // the change, so we must surface the failure rather than respond 200 OK.
            // The pending row is cleared so the next attempt starts from a clean
            // state — otherwise the (orphan, undeliverable) pending_email would
            // keep the UNIQUE (lower(pending_email)) index claimed for 24h.
            bool sent;
            try
            {
                sent = await emailService.SendEmailChangeConfirmationAsync(newEmail, displayName, token, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email change confirmation for user {UserId}", userId);
                sent = false;
            }

            if (!sent)
            {
                await using var clearCmd = new Npgsql.NpgsqlCommand(@"
                    UPDATE users
                    SET pending_email             = NULL,
                        email_change_token        = NULL,
                        email_change_requested_at = NULL,
                        updated_at                = NOW()
                    WHERE id = @id", db);
                clearCmd.Parameters.AddWithValue("id", userId);
                await clearCmd.ExecuteNonQueryAsync(ct);

                return Results.Problem(
                    title: "Email delivery failed",
                    detail: "Could not send the confirmation email. Please try again later.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            // Security: tell the CURRENT address that a change was requested (best-effort).
            _ = emailService.SendEmailChangeRequestedOldAddressAsync(currentEmail, displayName, newEmail);

            logger.LogInformation("Email change requested for user {UserId}: pending={NewEmail}", userId, newEmail);
            return Results.Ok(new { message = "Confirmation email sent. Check your new inbox and click the link to complete the change." });
        })
        .RequireAuthorization()
        .WithMetadata(new SkipTenantResolutionAttribute())
        .WithName("RequestEmailChange")
        .WithSummary("Request an email address change")
        .WithTags("Account");

        // GET /api/account/confirm-email?token=<uuid> — anonymous; confirms the pending email change.
        app.MapGet("/api/account/confirm-email", [AllowAnonymous] async (
            string? token,
            IDbConnectionFactory dbFactory,
            IKeycloakAdminService keycloakAdmin,
            IEmailService emailService,
            IConfiguration configuration,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            var frontendBaseUrl = GetFrontendBaseUrl(configuration);
            string Redirect(string status) => $"{frontendBaseUrl}/account?email-change={status}";

            if (string.IsNullOrWhiteSpace(token))
                return Results.Redirect(Redirect("invalid"));

            try
            {
                await using var db = dbFactory.CreateControlPlaneConnection();
                await db.OpenAsync();
                await using var tx = await db.BeginTransactionAsync(ct);

                string? keycloakId;
                Guid userId;
                string currentEmail;
                string? pendingEmail;
                await using (var findCmd = new Npgsql.NpgsqlCommand(@"
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
                    FOR UPDATE", db, tx))
                {
                    findCmd.Parameters.AddWithValue("token", token);
                    await using var reader = await findCmd.ExecuteReaderAsync(ct);
                    if (!await reader.ReadAsync(ct))
                        return Results.Redirect(Redirect("expired"));

                    userId = reader.GetGuid(0);
                    keycloakId = reader.IsDBNull(1) ? null : reader.GetString(1);
                    currentEmail = reader.GetString(2);
                    pendingEmail = reader.IsDBNull(3) ? null : reader.GetString(3);
                }

                if (string.IsNullOrEmpty(pendingEmail))
                {
                    logger.LogWarning("confirm-email: missing pending_email for token");
                    return Results.Redirect(Redirect("error"));
                }

                // Conflict pre-check: avoid calling Keycloak when the local UNIQUE (email) constraint
                // would reject the commit anyway. Without this, Keycloak would be updated and our DB
                // UPDATE would then fail with 23505, leaving the two stores divergent.
                await using (var conflictCmd = new Npgsql.NpgsqlCommand(@"
                    SELECT EXISTS (
                        SELECT 1 FROM users
                        WHERE id <> @id AND lower(email) = lower(@pending)
                    )", db, tx))
                {
                    conflictCmd.Parameters.AddWithValue("id", userId);
                    conflictCmd.Parameters.AddWithValue("pending", pendingEmail);
                    if ((bool)(await conflictCmd.ExecuteScalarAsync(ct))!)
                        return Results.Redirect(Redirect("conflict"));
                }

                // Atomic idempotency: a concurrent confirmation or a stale link returns 0 rows.
                // UNIQUE (email) on users may raise 23505 if another user already has this address.
                int updated;
                try
                {
                    await using var commitCmd = new Npgsql.NpgsqlCommand(@"
                        UPDATE users
                        SET email                      = pending_email,
                            pending_email              = NULL,
                            email_change_token         = NULL,
                            email_change_requested_at  = NULL,
                            updated_at                 = NOW()
                        WHERE id = @id
                          AND email_change_token = @token", db, tx);
                    commitCmd.Parameters.AddWithValue("id", userId);
                    commitCmd.Parameters.AddWithValue("token", token);
                    updated = await commitCmd.ExecuteNonQueryAsync(ct);
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == PgUniqueViolation)
                {
                    return Results.Redirect(Redirect("conflict"));
                }
                if (updated != 1)
                    return Results.Redirect(Redirect("expired"));

                await using (var identityCmd = new Npgsql.NpgsqlCommand(@"
                    UPDATE user_identities
                    SET provider_email = @email
                    WHERE user_id = @id
                      AND provider = 'keycloak'", db, tx))
                {
                    identityCmd.Parameters.AddWithValue("id", userId);
                    identityCmd.Parameters.AddWithValue("email", pendingEmail);
                    await identityCmd.ExecuteNonQueryAsync(ct);
                }

                // The DB update has passed local constraints inside the transaction.
                // If Keycloak fails, rollback keeps the old email authoritative and
                // preserves the pending token so the user can retry.
                await keycloakAdmin.UpdateEmailForAccountAsync(keycloakId, currentEmail, pendingEmail, ct);
                await tx.CommitAsync(ct);

                logger.LogInformation("Email change confirmed for user {UserId}: new email={PendingEmail}", userId, pendingEmail);
                // Confirm completion to the new address (best-effort).
                _ = emailService.SendEmailChangedAsync(pendingEmail, pendingEmail, pendingEmail);
                return Results.Redirect(Redirect("confirmed"));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing confirm-email token");
                return Results.Redirect(Redirect("error"));
            }
        })
        .WithName("ConfirmEmailChange")
        .WithSummary("Confirm email address change from confirmation email")
        .WithTags("Account")
        .WithMetadata(new SkipTenantResolutionAttribute());
    }

    // Resolves the user-facing frontend origin for browser redirects.
    // In production APP_BASE_URL is the Nginx origin that serves both the SPA
    // and the API, so it works directly. In local dev APP_BASE_URL points at
    // the API port (e.g. 5002) but the SPA runs on a different port (5174),
    // so we fall back to the first CORS-allowed origin, which is the value
    // configured for the dev frontend.
    private static string GetFrontendBaseUrl(IConfiguration configuration)
    {
        var corsOrigins = configuration[ConfigKeys.CorsAllowedOrigins];
        if (!string.IsNullOrWhiteSpace(corsOrigins))
        {
            var firstOrigin = corsOrigins
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(firstOrigin))
                return firstOrigin.TrimEnd('/');
        }
        return configuration.GetRequired(ConfigKeys.AppBaseUrl).TrimEnd('/');
    }
}
