using Api.Configuration;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Orkyo.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

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
            ILogger<EndpointLoggerCategory> logger) =>
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
                await using (var findCmd = AccountLifecycleCommandFactory.CreateSelectUserByConfirmTokenCommand(db, token))
                await using (var reader = await findCmd.ExecuteReaderAsync())
                {
                    record = await AccountLifecycleReaderFlow.ReadConfirmRecordAsync(reader);
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
                await using (var clearCmd = AccountLifecycleCommandFactory.CreateClearLifecycleStateCommand(db, record.UserId))
                {
                    await clearCmd.ExecuteNonQueryAsync();
                }

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
