using Api.Configuration;
using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Repositories;
using Api.Security;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orkyo.Shared;

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
            IPlatformUserRepository userRepository,
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
                var record = await userRepository.FindActiveLifecycleConfirmAsync(token, ct);

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
                await userRepository.ClearLifecycleStateAsync(record.UserId, ct);

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
    public static void MapAccountEmailChangeEndpoints(this WebApplication app)
    {
        // POST /api/account/email — authenticated; stores a pending change and sends confirmation to the new address.
        app.MapPost("/api/account/email", async (
            RequestEmailChangeRequest request,
            IValidator<RequestEmailChangeRequest> validator,
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IPlatformUserRepository userRepository,
            IKeycloakAdminService keycloakAdmin,
            IEmailService emailService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            accountGuard.EnsureCanMutateOwnAccount(principal);
            var userId = principal.RequireUserId();
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var newEmail = request.NewEmail.Trim().ToLowerInvariant();

                var userRow = await userRepository.GetEmailAndDisplayNameAsync(userId, ct);
                if (userRow is null)
                    return Results.NotFound();
                var (currentEmail, displayName) = userRow.Value;

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

                if (!await userRepository.SetPendingEmailChangeAsync(userId, newEmail, token, ct))
                    return ErrorResponses.Conflict("That email address is already in use.");

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
                    await userRepository.ClearPendingEmailChangeAsync(userId, ct);

                    return Results.Problem(
                        title: "Email delivery failed",
                        detail: "Could not send the confirmation email. Please try again later.",
                        statusCode: StatusCodes.Status502BadGateway);
                }

                // Security: tell the CURRENT address that a change was requested (best-effort).
                _ = emailService.SendEmailChangeRequestedOldAddressAsync(currentEmail, displayName, newEmail);

                logger.LogInformation("Email change requested for user {UserId}: pending={NewEmail}", userId, newEmail);
                return Results.Ok(new { message = "Confirmation email sent. Check your new inbox and click the link to complete the change." });
            }, logger, "request email change");
        })
        .RequireAuthorization()
        .WithMetadata(new SkipTenantResolutionAttribute())
        .WithName("RequestEmailChange")
        .WithSummary("Request an email address change")
        .WithTags("Account");

        // GET /api/account/confirm-email?token=<uuid> — anonymous; confirms the pending email change.
        app.MapGet("/api/account/confirm-email", [AllowAnonymous] async (
            string? token,
            IPlatformUserRepository userRepository,
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
                var result = await userRepository.ConfirmEmailChangeAsync(
                    token,
                    (keycloakId, currentEmail, pendingEmail, callCt) =>
                        keycloakAdmin.UpdateEmailForAccountAsync(keycloakId, currentEmail, pendingEmail, callCt),
                    ct);

                switch (result.Status)
                {
                    case EmailChangeConfirmStatus.NotFoundOrExpired:
                        return Results.Redirect(Redirect("expired"));
                    case EmailChangeConfirmStatus.MissingPendingEmail:
                        logger.LogWarning("confirm-email: missing pending_email for token");
                        return Results.Redirect(Redirect("error"));
                    case EmailChangeConfirmStatus.Conflict:
                        return Results.Redirect(Redirect("conflict"));
                }

                logger.LogInformation("Email change confirmed for user {UserId}: new email={PendingEmail}", result.UserId, result.PendingEmail);
                // Confirm completion to the new address (best-effort).
                _ = emailService.SendEmailChangedAsync(result.PendingEmail!, result.PendingEmail!, result.PendingEmail!);
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
