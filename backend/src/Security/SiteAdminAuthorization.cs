using Api.Helpers;
using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace Api.Security;

/// <summary>Names of the authorization policies foundation registers.</summary>
public static class AuthorizationPolicies
{
    /// <summary>The caller carries the Keycloak <c>site-admin</c> realm role.</summary>
    public const string SiteAdmin = "SiteAdmin";
}

/// <summary>Requirement behind <see cref="AuthorizationPolicies.SiteAdmin"/>.</summary>
public sealed class SiteAdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Decides <see cref="SiteAdminRequirement"/> from the token's realm roles, the same source
/// <c>ContextEnrichmentMiddleware</c> later copies into <c>ICurrentPrincipal.IsSiteAdmin</c>.
/// It runs in <c>UseAuthorization</c>, before the body of a request is bound, so a caller without
/// the role learns nothing about the request's validation rules.
/// </summary>
public sealed class SiteAdminAuthorizationHandler : AuthorizationHandler<SiteAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SiteAdminRequirement requirement)
    {
        if (KeycloakTokenProfile.FromPrincipal(context.User).IsSiteAdmin)
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Answers a failed <see cref="AuthorizationPolicies.SiteAdmin"/> check with the same problem
/// bodies the rest of the API uses (401 <c>session_expired</c>, 403 <c>forbidden</c>) instead of
/// the authentication scheme's challenge, which for the cookie scheme would be a redirect.
/// Every other policy keeps the framework's default handling.
/// </summary>
public sealed class OrkyoAuthorizationResultHandler(ILogger<OrkyoAuthorizationResultHandler> logger) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded || !policy.Requirements.Any(r => r is SiteAdminRequirement))
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (authorizeResult.Challenged)
        {
            logger.LogWarning("Site admin check failed: user not authenticated");
            await ErrorResponses.Unauthorized().ExecuteAsync(context);
            return;
        }

        logger.LogWarning("Site admin check failed: {User} is not a site admin", context.User.Identity?.Name ?? "(anonymous)");
        await ErrorResponses.Forbidden().ExecuteAsync(context);
    }
}
