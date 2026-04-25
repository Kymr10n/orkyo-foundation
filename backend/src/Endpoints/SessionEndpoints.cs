using System.Security.Claims;
using Api.Models;
using Api.Services;
using Api.Middleware;
using Api.Helpers;
using Api.Security;
using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SessionEndpoints
{
    // Non-static marker for ILogger<T> — static classes can't be type arguments.
    private sealed class Log { }
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var session = app.MapGroup("/api/session")
            .RequireAuthorization()
            .WithMetadata(new SkipTenantResolutionAttribute());

        // GET /api/session/bootstrap - Called after Keycloak login to link identity and get tenant memberships
        session.MapGet("/bootstrap", async (
            HttpContext ctx,
            IIdentityLinkService identityLinkService,
            ISessionService sessionService,
            ILogger<Log> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(
                async () =>
                {
                    // Extract token profile from the authenticated user (Keycloak token)
                    var tokenProfile = KeycloakTokenProfile.FromPrincipal(ctx.User);

                    if (!tokenProfile.IsValid || string.IsNullOrEmpty(tokenProfile.Subject))
                    {
                        logger.LogWarning("Bootstrap called without valid Keycloak token");
                        return ProblemDetailsHelper.AuthProblem(
                            ProblemDetailsHelper.AuthCodes.InvalidToken,
                            "Invalid authentication token",
                            "Missing 'sub' claim in token");
                    }

                    logger.LogInformation(
                        "Session bootstrap for sub={Sub}, email={Email}",
                        tokenProfile.Subject,
                        tokenProfile.Email);

                    // Link identity (idempotent - will return existing user if already linked)
                    var externalToken = tokenProfile.ToExternalIdentityToken();
                    if (externalToken == null)
                    {
                        return ProblemDetailsHelper.AuthProblem(
                            ProblemDetailsHelper.AuthCodes.InvalidToken,
                            "Invalid authentication token",
                            "Could not extract identity from token");
                    }

                    var linkResult = await identityLinkService.LinkIdentityAsync(externalToken);

                    if (!linkResult.Success || !linkResult.UserId.HasValue)
                    {
                        logger.LogWarning("Identity link failed: {Error}", linkResult.Error);
                        return ProblemDetailsHelper.AuthProblem(
                            linkResult.ErrorCode ?? ProblemDetailsHelper.AuthCodes.IdentityNotLinked,
                            "Authentication failed",
                            linkResult.Error);
                    }

                    // Sync display name from Keycloak token if it changed since last login
                    if (!string.IsNullOrEmpty(externalToken.DisplayName)
                        && externalToken.DisplayName != linkResult.DisplayName)
                    {
                        await sessionService.UpdateDisplayNameAsync(
                            linkResult.UserId.Value, externalToken.DisplayName);
                    }

                    // Build session response
                    var result = await sessionService.BuildSessionResponseAsync(linkResult.UserId.Value);

                    // Add site-admin flag from token (realm role, not from DB)
                    var responseWithAdmin = result != null
                        ? result with { IsSiteAdmin = tokenProfile.IsSiteAdmin }
                        : null;

                    logger.LogInformation(
                        "Bootstrap response for user {UserId}: TosRequired={TosRequired}, RequiredVersion={RequiredVersion}, TenantsCount={TenantsCount}, IsSiteAdmin={IsSiteAdmin}",
                        linkResult.UserId.Value,
                        result?.TosRequired ?? false,
                        result?.RequiredTosVersion ?? "null",
                        result?.Tenants?.Count ?? 0,
                        tokenProfile.IsSiteAdmin);

                    return Results.Ok(responseWithAdmin);
                },
                logger,
                "session bootstrap",
                new { sub = KeycloakTokenProfile.FromPrincipal(ctx.User).Subject }
            );
        })
        .WithName("SessionBootstrap")
        .WithSummary("Bootstrap session after Keycloak login")
        .WithDescription("Links Keycloak identity to internal user and returns tenant memberships and ToS status.")
        .WithTags("Session")
        .RequireRateLimiting("session-bootstrap");

        // GET /api/session/me - Get current user info
        session.MapGet("/me", async (
            ICurrentPrincipal currentPrincipal,
            ISessionService sessionService,
            ILogger<Log> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(
                async () =>
                {
                    if (!currentPrincipal.IsAuthenticated)
                    {
                        return Results.Unauthorized();
                    }

                    var sessionInfo = await sessionService.GetSessionByUserIdAsync(currentPrincipal.UserId);
                    if (sessionInfo == null)
                    {
                        return ErrorResponses.NotFound("User");
                    }
                    var me = new MeResponse
                    {
                        Id = sessionInfo.User.Id,
                        Email = sessionInfo.User.Email,
                        DisplayName = sessionInfo.User.DisplayName,
                        KeycloakId = sessionInfo.User.KeycloakId,
                        HasSeenTour = sessionInfo.User.HasSeenTour,
                        CreatedAt = sessionInfo.User.CreatedAt,
                        LastLoginAt = sessionInfo.User.LastLoginAt,
                        Tenants = sessionInfo.Tenants
                    };
                    return Results.Ok(me);
                },
                logger,
                "get current user"
            );
        })
        .WithName("SessionMe")
        .WithSummary("Get current user information")
        .WithDescription("Returns the current authenticated user's information including tenant memberships.")
        .WithTags("Session");

        // POST /api/session/tour/seen - Mark onboarding tour as seen
        session.MapPost("/tour/seen", async (
            ICurrentPrincipal currentPrincipal,
            ISessionService sessionService,
            ILogger<Log> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(
                async () =>
                {
                    if (!currentPrincipal.IsAuthenticated)
                        return Results.Unauthorized();

                    await sessionService.MarkTourSeenAsync(currentPrincipal.UserId);
                    return Results.Ok(new { marked = true });
                },
                logger,
                "mark tour seen"
            );
        })
        .WithName("MarkTourSeen")
        .WithSummary("Mark onboarding tour as seen")
        .WithDescription("Marks the onboarding tour as seen for the current user. Global per user, not per tenant.")
        .WithTags("Session");

        // POST /api/session/tos/accept - Accept Terms of Service
        session.MapPost("/tos/accept", async (
            HttpContext ctx,
            TosAcceptRequest request,
            ICurrentPrincipal currentPrincipal,
            ISessionService sessionService,
            ILogger<Log> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(
                async () =>
                {
                    if (!currentPrincipal.IsAuthenticated)
                    {
                        return Results.Unauthorized();
                    }

                    // Validate version matches required
                    var requiredVersion = sessionService.GetRequiredTosVersion();
                    if (string.IsNullOrEmpty(requiredVersion))
                    {
                        return Results.BadRequest(new { error = "No ToS version required" });
                    }

                    if (request.TosVersion != requiredVersion)
                    {
                        return Results.BadRequest(new { error = $"Invalid ToS version. Required: {requiredVersion}" });
                    }

                    // Get IP and user agent for audit
                    var ipAddress = ctx.Connection.RemoteIpAddress?.ToString();
                    var userAgent = ctx.Request.Headers.UserAgent.FirstOrDefault();

                    await sessionService.AcceptTosAsync(
                        currentPrincipal.UserId,
                        request.TosVersion,
                        ipAddress,
                        userAgent);

                    return Results.Ok(new { accepted = true, tosVersion = request.TosVersion });
                },
                logger,
                "accept ToS"
            );
        })
        .WithName("AcceptToS")
        .WithSummary("Accept Terms of Service")
        .WithDescription("Records user acceptance of the specified ToS version.")
        .WithTags("Session");

        // Public registration endpoint (no auth required)
        var publicAuth = app.MapGroup("/api/auth")
            .AllowAnonymous()
            .WithMetadata(new SkipTenantResolutionAttribute());

        // POST /api/auth/create-account - Create account in Keycloak
        publicAuth.MapPost("/create-account", async (
            CreateAccountRequest request,
            IKeycloakAdminService keycloakAdminService,
            ILogger<Log> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(
                async () =>
                {
                    // Validate request
                    if (string.IsNullOrWhiteSpace(request.Email))
                    {
                        return Results.BadRequest(new { error = "Email is required" });
                    }

                    if (string.IsNullOrWhiteSpace(request.Password))
                    {
                        return Results.BadRequest(new { error = "Password is required" });
                    }

                    if (request.Password.Length < TenantSettings.DefaultPasswordMinLength)
                    {
                        return Results.BadRequest(new { error = $"Password must be at least {TenantSettings.DefaultPasswordMinLength} characters" });
                    }

                    // Validate email format
                    if (!IsValidEmail(request.Email))
                    {
                        return Results.BadRequest(new { error = "Invalid email format" });
                    }

                    logger.LogInformation("Creating account for {Email}", request.Email);

                    // Split display name into first/last if provided
                    string? firstName = null;
                    string? lastName = null;
                    if (!string.IsNullOrWhiteSpace(request.DisplayName))
                    {
                        var parts = request.DisplayName.Trim().Split(' ', 2);
                        firstName = parts[0];
                        lastName = parts.Length > 1 ? parts[1] : null;
                    }

                    await keycloakAdminService.CreateUserAsync(
                        request.Email,
                        request.Password,
                        firstName,
                        lastName
                    );

                    logger.LogInformation("Account created for {Email}", request.Email);
                    return Results.Ok(new
                    {
                        message = "Account created. Please check your email to verify your account."
                    });
                },
                logger,
                "create account",
                new { email = request.Email }
            );
        })
        .AllowAnonymous()
        .WithName("CreateAccount")
        .WithSummary("Create a new account")
        .WithDescription("Creates a new user in Keycloak and sends a verification email.")
        .WithTags("Auth");
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}

public record TosAcceptRequest
{
    public required string TosVersion { get; init; }
}

public record CreateAccountRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public string? DisplayName { get; init; }
}

// Response models for session endpoints
public record SessionBootstrapResponse
{
    public required UserInfo User { get; init; }
    public bool TosRequired { get; init; }
    public string? RequiredTosVersion { get; init; }
    public List<TenantMembershipInfo> Tenants { get; init; } = new();
    public string? SuggestedTenantSlug { get; init; }
    public bool IsSiteAdmin { get; init; }
}

/// <summary>
/// Flat response for GET /api/session/me — mirrors the frontend AppUser interface
/// with tenants included at the root level.
/// </summary>
public record MeResponse
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public string? KeycloakId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public bool HasSeenTour { get; init; }
    public List<TenantMembershipInfo> Tenants { get; init; } = new();
}

public record UserInfo
{
    public Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public string? KeycloakId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public bool HasSeenTour { get; init; }
}

public record TenantMembershipInfo
{
    public Guid TenantId { get; init; }
    public required string Slug { get; init; }
    public required string DisplayName { get; init; }
    public required string Role { get; init; }
    public required string State { get; init; }
    public bool IsOwner { get; init; }
    public required string Tier { get; init; }
    public string? SuspensionReason { get; init; }
    public DateTime? SuspendedAt { get; init; }
    public bool CanReactivate { get; init; }
}
