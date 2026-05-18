using Api.Configuration;
using Api.Integrations.Keycloak;
using Api.Middleware;
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
                      AND lifecycle_status IS NOT NULL", db))
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
                    logger.LogWarning("confirm-activity: token not found or already used: {Token}", token);
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
