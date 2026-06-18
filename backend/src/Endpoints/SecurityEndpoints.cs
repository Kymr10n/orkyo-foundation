using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Middleware;
using Api.Models;
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
            IKeycloakAdminService keycloakService,
            ITenantSettingsService settingsService,
            ChangePasswordRequest request,
            IValidator<ChangePasswordRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var sub = principal.RequireExternalSubject();

                var settings = await settingsService.GetSettingsAsync(ct);
                if (request.NewPassword!.Length < settings.PasswordMinLength)
                    return ErrorResponses.BadRequest($"New password must be at least {settings.PasswordMinLength} characters");

                await keycloakService.ChangePasswordAsync(sub, request.CurrentPassword!, request.NewPassword, ct);
                logger.LogInformation("Password changed for user {Sub}", sub);
                return Results.Ok(new { message = "Password changed successfully" });
            }, logger, "change password");
        })
        .WithName("ChangePassword")
        .WithSummary("Change user password")
        .WithTags("Security")
        .RequireRateLimiting("password-change");

        security.MapGet("/sessions", async (
            HttpContext ctx,
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var sub = principal.RequireExternalSubject();
                var sessions = await keycloakService.GetUserSessionsAsync(sub, ct);
                var tokenProfile = KeycloakTokenProfile.FromPrincipal(ctx.User);
                var currentSessionId = tokenProfile.SessionId;
                var response = sessions.Select(s => new SessionResponse
                {
                    Id = s.Id,
                    IpAddress = s.IpAddress,
                    StartTime = s.StartTime,
                    LastAccessTime = s.LastAccessTime,
                    IsCurrent = s.Id == currentSessionId,
                    Clients = s.Clients?.Values.ToList() ?? new List<string>()
                }).ToList();
                return Results.Ok(response);
            }, logger, "list sessions");
        })
        .WithName("ListSessions")
        .WithSummary("List active sessions")
        .WithTags("Security");

        security.MapDelete("/sessions/{sessionId}", async (
            string sessionId,
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var sub = principal.RequireExternalSubject();
                var sessions = await keycloakService.GetUserSessionsAsync(sub, ct);
                if (!sessions.Any(s => s.Id == sessionId))
                    return ErrorResponses.NotFoundMessage("Session not found");
                await keycloakService.RevokeSessionAsync(sessionId, ct);
                logger.LogInformation("Session {SessionId} revoked by user {Sub}", sessionId, sub);
                return Results.Ok(new { message = "Session revoked" });
            }, logger, "revoke session", new { sessionId });
        })
        .WithName("RevokeSession")
        .WithSummary("Revoke a specific session")
        .WithTags("Security");

        security.MapPost("/logout-all", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var sub = principal.RequireExternalSubject();
                await keycloakService.LogoutAllSessionsAsync(sub, ct);
                logger.LogInformation("All sessions terminated for user {Sub}", sub);
                return Results.Ok(new { message = "Logged out from all sessions" });
            }, logger, "logout all sessions");
        })
        .WithName("LogoutAll")
        .WithSummary("Logout from all sessions")
        .WithTags("Security");

        security.MapGet("/security-info", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var sub = principal.RequireExternalSubject();
                var federation = await keycloakService.GetUserFederationStatusAsync(sub, ct);
                return Results.Ok(new SecurityInfoResponse
                {
                    IsFederated = federation.IsFederated,
                    IdentityProvider = federation.IdentityProvider,
                    CanChangePassword = !federation.IsFederated
                });
            }, logger, "get security info");
        })
        .WithName("GetSecurityInfo")
        .WithSummary("Get security settings info")
        .WithTags("Security");

        security.MapGet("/mfa-status", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
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
            }, logger, "get MFA status");
        })
        .WithName("GetMfaStatus")
        .WithSummary("Get MFA status")
        .WithTags("Security");

        security.MapPost("/mfa", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var sub = principal.RequireExternalSubject();
                var status = await keycloakService.GetMfaStatusAsync(sub, ct);
                if (status.TotpEnabled)
                    return ErrorResponses.BadRequest("MFA is already enabled");
                await keycloakService.EnableMfaAsync(sub, ct);
                return Results.Ok(new { message = "MFA enrollment enabled. You will be prompted to set up TOTP on your next login." });
            }, logger, "enable MFA");
        })
        .WithName("EnableMfa")
        .WithSummary("Enable MFA enrollment")
        .WithTags("Security");

        security.MapDelete("/mfa", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
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
                return Results.Ok(new { message = "MFA has been removed. You can re-enable it at any time from your security settings." });
            }, logger, "remove MFA");
        })
        .WithName("RemoveMfa")
        .WithSummary("Remove MFA")
        .WithTags("Security");

        security.MapGet("/profile", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
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
            }, logger, "get user profile");
        })
        .WithName("GetUserProfile")
        .WithSummary("Get user profile")
        .WithTags("Security");

        security.MapPut("/profile", async (
            ICurrentPrincipal principal,
            IKeycloakAdminService keycloakService,
            ISessionService sessionService,
            UpdateProfileRequest request,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
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
            }, logger, "update user profile");
        })
        .WithName("UpdateUserProfile")
        .WithSummary("Update user profile")
        .WithTags("Security");
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
}

public record SecurityInfoResponse
{
    public bool IsFederated { get; init; }
    public string? IdentityProvider { get; init; }
    public bool CanChangePassword { get; init; }
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
