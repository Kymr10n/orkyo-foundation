using Api.Configuration;
using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SecurityEndpoints
{
    public static void MapSecurityEndpoints(this WebApplication app)
    {
        var security = app.MapGroup("/api/account")
            .RequireAuthorization()
            .WithMetadata(new SkipTenantResolutionAttribute());

        security.MapPost("/password", async (
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            ITenantSettingsService settingsService,
            IEmailService emailService,
            ChangePasswordRequest request,
            IValidator<ChangePasswordRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            accountGuard.EnsureCanMutateOwnAccount(principal);
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var sub = principal.RequireExternalSubject();

                var settings = await settingsService.GetSettingsAsync(ct);
                if (request.NewPassword!.Length < settings.PasswordMinLength)
                    return ErrorResponses.BadRequest($"New password must be at least {settings.PasswordMinLength} characters");

                await keycloakService.ChangePasswordAsync(sub, request.CurrentPassword!, request.NewPassword, ct);
                logger.LogInformation("Password changed for user {Sub}", sub);
                // Security confirmation (best-effort, non-blocking).
                _ = emailService.SendPasswordChangedAsync(principal.Email, principal.DisplayName ?? principal.Email);
                return Results.Ok(new { message = "Password changed successfully" });
            }, logger, "change password");
        })
        .WithName("ChangePassword")
        .WithSummary("Change user password")
        .WithTags("Security")
        .RequireRateLimiting(FoundationRateLimitPolicies.PasswordChange);

        security.MapGet("/sessions", async (
            HttpContext ctx,
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            IUserSessionService userSessionService,
            CancellationToken ct) =>
        {
            var sub = principal.RequireExternalSubject();
            var userId = principal.RequireUserId();
            // Keycloak stays the source of truth for what is live + revocable.
            var sessions = await keycloakService.GetUserSessionsAsync(sub, ct);
            var currentSessionId = KeycloakTokenProfile.FromPrincipal(ctx.User).SessionId;

            // Enrich with the real device/IP we captured at login, joined by sid.
            var captured = (await userSessionService.GetByUserAsync(userId, ct))
                .ToDictionary(r => r.KeycloakSessionId);

            // Drop captured rows whose Keycloak session is gone (aged out / revoked elsewhere).
            await userSessionService.PruneExceptAsync(userId, sessions.Select(s => s.Id).ToList(), ct);

            var response = sessions.Select(s =>
            {
                captured.TryGetValue(s.Id, out var meta);
                return new SessionResponse
                {
                    Id = s.Id,
                    // Real client IP when known; fall back to Keycloak's value for older sessions.
                    IpAddress = meta?.IpAddress ?? s.IpAddress,
                    StartTime = s.StartTime,
                    LastAccessTime = s.LastAccessTime,
                    IsCurrent = s.Id == currentSessionId,
                    Clients = s.Clients?.Values.ToList() ?? new List<string>(),
                    Browser = meta?.Browser,
                    OperatingSystem = meta?.OperatingSystem,
                    DeviceType = meta?.DeviceType,
                    DeviceLabel = BuildDeviceLabel(meta),
                };
            }).ToList();

            // Shared/locked accounts (the demo identity) must never expose other visitors' sessions —
            // their IPs/devices belong to different people. Return only the caller's own current session.
            // (No resolvable current sid → empty, which fails safe rather than leaking strangers.)
            if (accountGuard.IsAccountLocked(principal))
                response = response.Where(r => r.IsCurrent).ToList();

            return Results.Ok(response);
        })
        .WithName("ListSessions")
        .WithSummary("List active sessions")
        .WithTags("Security");

        security.MapDelete("/sessions/{sessionId}", async (
            string sessionId,
            HttpContext ctx,
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            IUserSessionService userSessionService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            var sub = principal.RequireExternalSubject();
            // On a shared/locked account, other visitors' sessions are invisible and untouchable —
            // only the caller's own current session may be revoked (report others as not found so their
            // existence isn't confirmed).
            if (accountGuard.IsAccountLocked(principal)
                && sessionId != KeycloakTokenProfile.FromPrincipal(ctx.User).SessionId)
                return ErrorResponses.NotFoundMessage("Session not found");

            var sessions = await keycloakService.GetUserSessionsAsync(sub, ct);
            if (!sessions.Any(s => s.Id == sessionId))
                return ErrorResponses.NotFoundMessage("Session not found");
            await keycloakService.RevokeSessionAsync(sessionId, ct);
            await userSessionService.RemoveAsync(sessionId, ct);
            logger.LogInformation("Session {SessionId} revoked by user {Sub}", sessionId, sub);
            return Results.Ok(new { message = "Session revoked" });
        })
        .WithName("RevokeSession")
        .WithSummary("Revoke a specific session")
        .WithTags("Security");

        security.MapPost("/logout-all", async (
            HttpContext ctx,
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            IUserSessionService userSessionService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            var sub = principal.RequireExternalSubject();

            // On a shared/locked account a global logout would sign out every other visitor. Scope it to
            // the caller's own current session so "sign out everywhere" only signs out themselves.
            if (accountGuard.IsAccountLocked(principal))
            {
                var currentSessionId = KeycloakTokenProfile.FromPrincipal(ctx.User).SessionId;
                if (!string.IsNullOrEmpty(currentSessionId))
                {
                    await keycloakService.RevokeSessionAsync(currentSessionId, ct);
                    await userSessionService.RemoveAsync(currentSessionId, ct);
                }
                logger.LogInformation("Locked account: scoped logout to current session for user {Sub}", sub);
                return Results.Ok(new { message = "Logged out from all sessions" });
            }

            await keycloakService.LogoutAllSessionsAsync(sub, ct);
            await userSessionService.RemoveAllForUserAsync(principal.RequireUserId(), ct);
            logger.LogInformation("All sessions terminated for user {Sub}", sub);
            return Results.Ok(new { message = "Logged out from all sessions" });
        })
        .WithName("LogoutAll")
        .WithSummary("Logout from all sessions")
        .WithTags("Security");

        security.MapGet("/security-info", async (
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            CancellationToken ct) =>
        {
            var sub = principal.RequireExternalSubject();
            var federation = await keycloakService.GetUserFederationStatusAsync(sub, ct);
            var accountLocked = accountGuard.IsAccountLocked(principal);
            return Results.Ok(new SecurityInfoResponse
            {
                IsFederated = federation.IsFederated,
                IdentityProvider = federation.IdentityProvider,
                // A locked (shared demo) account cannot change its own password regardless of federation.
                CanChangePassword = !federation.IsFederated && !accountLocked,
                AccountLocked = accountLocked
            });
        })
        .WithName("GetSecurityInfo")
        .WithSummary("Get security settings info")
        .WithTags("Security");

        security.MapGet("/mfa-status", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct) =>
        {
            var sub = principal.RequireExternalSubject();
            var status = await keycloakService.GetMfaStatusAsync(sub, ct);
            return Results.Ok(new MfaStatusResponse
            {
                TotpEnabled = status.TotpEnabled,
                TotpCredentialId = status.TotpCredentialId,
                TotpCreatedDate = status.TotpCreatedDate,
                TotpLabel = status.TotpLabel,
                RecoveryCodesConfigured = status.RecoveryCodesConfigured,
            });
        })
        .WithName("GetMfaStatus")
        .WithSummary("Get MFA status")
        .WithTags("Security");

        security.MapPost("/mfa", async (
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            IEmailService emailService,
            CancellationToken ct) =>
        {
            accountGuard.EnsureCanMutateOwnAccount(principal);
            var sub = principal.RequireExternalSubject();
            var status = await keycloakService.GetMfaStatusAsync(sub, ct);
            if (status.TotpEnabled)
                return ErrorResponses.BadRequest("MFA is already enabled");
            await keycloakService.EnableMfaAsync(sub, ct);
            _ = emailService.SendMfaChangedAsync(principal.Email, principal.DisplayName ?? principal.Email, enabled: true);
            return Results.Ok(new { message = "MFA enrollment enabled. You will be prompted to set up TOTP on your next login." });
        })
        .WithName("EnableMfa")
        .WithSummary("Enable MFA enrollment")
        .WithTags("Security");

        security.MapDelete("/mfa", async (
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            IEmailService emailService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            accountGuard.EnsureCanMutateOwnAccount(principal);
            var sub = principal.RequireExternalSubject();
            var status = await keycloakService.GetMfaStatusAsync(sub, ct);
            if (!status.TotpEnabled || string.IsNullOrEmpty(status.TotpCredentialId))
                return ErrorResponses.BadRequest("MFA is not enabled");
            await keycloakService.DeleteUserCredentialAsync(sub, status.TotpCredentialId, ct);
            if (status.RecoveryCodesConfigured && !string.IsNullOrEmpty(status.RecoveryCodesCredentialId))
            {
                try { await keycloakService.DeleteUserCredentialAsync(sub, status.RecoveryCodesCredentialId, ct); }
                catch (KeycloakAdminException ex) { logger.LogWarning(ex, "Failed to remove recovery codes for user {Sub}", sub); }
            }
            logger.LogInformation("MFA removed for user {Sub}", sub);
            _ = emailService.SendMfaChangedAsync(principal.Email, principal.DisplayName ?? principal.Email, enabled: false);
            return Results.Ok(new { message = "MFA has been removed. You can re-enable it at any time from your security settings." });
        })
        .WithName("RemoveMfa")
        .WithSummary("Remove MFA")
        .WithTags("Security");

        security.MapGet("/profile", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct) =>
        {
            var sub = principal.RequireExternalSubject();
            var profile = await keycloakService.GetUserProfileAsync(sub, ct);
            return Results.Ok(new UserProfileResponse
            {
                Email = profile.Email,
                FirstName = profile.FirstName,
                LastName = profile.LastName,
                EmailVerified = profile.EmailVerified,
            });
        })
        .WithName("GetUserProfile")
        .WithSummary("Get user profile")
        .WithTags("Security");

        security.MapPut("/profile", async (
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IKeycloakAdminService keycloakService,
            ISessionService sessionService,
            UpdateProfileRequest request,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            accountGuard.EnsureCanMutateOwnAccount(principal);
            var sub = principal.RequireExternalSubject();
            if (string.IsNullOrWhiteSpace(request.FirstName) && string.IsNullOrWhiteSpace(request.LastName))
                return ErrorResponses.BadRequest("At least one name field is required");
            var firstName = request.FirstName?.Trim() ?? string.Empty;
            var lastName = request.LastName?.Trim() ?? string.Empty;
            await keycloakService.UpdateUserProfileAsync(sub, firstName, lastName, ct);
            var displayName = $"{firstName} {lastName}".Trim();
            await sessionService.UpdateDisplayNameAsync(principal.RequireUserId(), displayName, ct);
            logger.LogInformation("Profile updated for user {Sub}", sub);
            return Results.Ok(new { message = "Profile updated successfully", displayName });
        })
        .WithName("UpdateUserProfile")
        .WithSummary("Update user profile")
        .WithTags("Security");

        security.MapGet("/notification-preferences", async (
            ICurrentPrincipal principal,
            IPlatformUserRepository userRepository,
            CancellationToken ct) =>
        {
            var userId = principal.RequireUserId();
            var optOut = await userRepository.GetAnnouncementEmailOptOutAsync(userId, ct);
            if (optOut is null)
                return ErrorResponses.NotFoundMessage("User not found");
            return Results.Ok(new NotificationPreferencesResponse { AnnouncementEmailOptOut = optOut.Value });
        })
        .WithName("GetNotificationPreferences")
        .WithSummary("Get notification preferences")
        .WithTags("Security");

        security.MapPut("/notification-preferences", async (
            ICurrentPrincipal principal,
            IAccountMutationGuard accountGuard,
            IPlatformUserRepository userRepository,
            UpdateNotificationPreferencesRequest request,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            accountGuard.EnsureCanMutateOwnAccount(principal);
            var userId = principal.RequireUserId();
            await userRepository.SetAnnouncementEmailOptOutAsync(userId, request.AnnouncementEmailOptOut, ct);
            logger.LogInformation("Announcement email opt-out set to {OptOut} for user {UserId}",
                request.AnnouncementEmailOptOut, userId);
            return Results.Ok(new { message = "Notification preferences updated" });
        })
        .WithName("UpdateNotificationPreferences")
        .WithSummary("Update notification preferences")
        .WithTags("Security");
    }

    /// <summary>
    /// Human-friendly device label from captured metadata, e.g. "Chrome on Windows".
    /// Returns null when nothing was captured (the FE falls back to the client list).
    /// </summary>
    private static string? BuildDeviceLabel(UserSessionRow? meta)
    {
        if (meta is null)
            return null;
        if (meta.Browser is { Length: > 0 } b && meta.OperatingSystem is { Length: > 0 } o)
            return $"{b} on {o}";
        return meta.Browser ?? meta.OperatingSystem;
    }
}
// ChangePasswordRequest moved to Api.Models (Core)

public record SessionResponse
{
    public string Id { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime LastAccessTime { get; init; }
    public bool IsCurrent { get; init; }
    public List<string> Clients { get; init; } = new();

    // Captured device metadata (null for sessions predating capture).
    public string? Browser { get; init; }
    public string? OperatingSystem { get; init; }
    public string? DeviceType { get; init; }
    public string? DeviceLabel { get; init; }
}

public record SecurityInfoResponse
{
    public bool IsFederated { get; init; }
    public string? IdentityProvider { get; init; }
    public bool CanChangePassword { get; init; }

    /// <summary>
    /// True when this is a shared/locked identity (e.g. the public demo account) whose
    /// credentials and profile cannot be changed via self-service. The frontend hides the
    /// password/email/profile/MFA edit affordances when set.
    /// </summary>
    public bool AccountLocked { get; init; }
}

public record MfaStatusResponse
{
    public bool TotpEnabled { get; init; }
    public string? TotpCredentialId { get; init; }
    public DateTime? TotpCreatedDate { get; init; }
    public string? TotpLabel { get; init; }
    public bool RecoveryCodesConfigured { get; init; }
}

public record UpdateProfileRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

public record UserProfileResponse
{
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
}

public record NotificationPreferencesResponse
{
    public bool AnnouncementEmailOptOut { get; init; }
}

public record UpdateNotificationPreferencesRequest
{
    public bool AnnouncementEmailOptOut { get; init; }
}
