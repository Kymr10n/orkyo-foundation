using System.Security.Claims;
using Api.Constants;
using Api.Integrations.Keycloak;
using Api.Security;
using Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Orkyo.Shared;

namespace Api.Middleware;

/// <summary>
/// Middleware that enriches the request context with security information.
/// Resolves:
/// - PrincipalContext (authenticated user identity)
/// - TenantContext (resolved tenant)
/// - AuthorizationContext (user's role in tenant)
///
/// Also ensures user stub exists in tenant database for FK references.
/// All identity linking happens explicitly in the bootstrap endpoint.
///
/// PERF: Caches principal lookups and tenant roles for 5 minutes to avoid
/// repeated DB queries (identity rarely changes during a session).
/// </summary>
public sealed class ContextEnrichmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ContextEnrichmentMiddleware> _logger;

    // Cache: externalSubject → PrincipalContext (5-min absolute expiry, 5000 entry limit)
    private static readonly MemoryCache _principalCache = new(new MemoryCacheOptions
    {
        SizeLimit = 5_000
    });

    // Cache: "userId:tenantId" → TenantRole (5-min absolute expiry, 10000 entry limit)
    private static readonly MemoryCache _roleCache = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000
    });

    // Track which user stubs have already been created (INSERT ON CONFLICT is idempotent, so the
    // worst case of a stale entry is one redundant no-op INSERT after an eviction).
    private static readonly MemoryCache _stubsCreated = new(new MemoryCacheOptions
    {
        SizeLimit = 10_000
    });

    private static readonly TimeSpan CacheTtl = TimePolicyConstants.CacheTtl;

    /// <summary>Clear all caches (for integration tests).</summary>
    public static void ClearCache()
    {
        _principalCache.Compact(1.0);
        _roleCache.Compact(1.0);
        _stubsCreated.Compact(1.0);
    }

    public ContextEnrichmentMiddleware(
        RequestDelegate next,
        ILogger<ContextEnrichmentMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        CurrentPrincipal currentPrincipal,
        CurrentTenant currentTenant,
        CurrentAuthorizationContext currentAuthContext,
        IIdentityLinkService identityLinkService,
        ITenantUserService tenantUserService,
        IBreakGlassSessionStore breakGlassSessionService)
    {
        // Step 1: Resolve principal from Keycloak token
        var principal = await ResolvePrincipalAsync(context, identityLinkService);
        currentPrincipal.SetContext(principal);

        // Step 2: Get tenant context (already resolved by TenantMiddleware)
        var tenantContext = context.Items[HttpContextItemKeys.TenantContext] as TenantContext;
        if (tenantContext != null)
        {
            currentTenant.SetContext(tenantContext);

            // Step 3: Resolve authorization context (role in tenant)
            if (principal.IsAuthenticated)
            {
                var authContext = await ResolveAuthorizationContextAsync(
                    identityLinkService,
                    breakGlassSessionService,
                    principal.UserId,
                    tenantContext,
                    principal.IsSiteAdmin);
                currentAuthContext.SetContext(authContext);

                // Step 4: Ensure user stub exists in tenant DB for FK references
                // Tracked in-memory: skip redundant INSERT ON CONFLICT after first success
                if (authContext.IsMember)
                {
                    var stubKey = $"{principal.UserId}:{tenantContext.TenantId}";
                    if (_stubsCreated.Get(stubKey) == null)
                    {
                        _stubsCreated.Set(stubKey, true, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = CacheTtl,
                            Size = 1
                        });
                        var orgContext = new OrgContext
                        {
                            OrgId = tenantContext.TenantId,
                            OrgSlug = tenantContext.TenantSlug,
                            DbConnectionString = tenantContext.TenantDbConnectionString,
                        };
                        await tenantUserService.CreateUserStubInTenantDatabaseAsync(
                            orgContext, principal.UserId, principal.Email);
                    }
                }

                _logger.LogDebug(
                    "Context enriched: User={UserId}, Tenant={TenantSlug}, Role={Role}",
                    principal.UserId, tenantContext.TenantSlug, authContext.Role);
            }
        }

        await _next(context);
    }

    private async Task<PrincipalContext> ResolvePrincipalAsync(
        HttpContext context,
        IIdentityLinkService identityLinkService)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return PrincipalContext.Anonymous;
        }

        // Try Keycloak token first
        var tokenProfile = KeycloakTokenProfile.FromPrincipal(context.User);

        if (tokenProfile.IsValid && !string.IsNullOrEmpty(tokenProfile.Subject))
        {
            return await ResolveKeycloakPrincipalAsync(tokenProfile, identityLinkService);
        }

        // Fallback to legacy JWT claims (for tests and backward compatibility)
        return ResolveLegacyPrincipal(context.User);
    }

    private async Task<PrincipalContext> ResolveKeycloakPrincipalAsync(
        KeycloakTokenProfile tokenProfile,
        IIdentityLinkService identityLinkService)
    {
        var subject = tokenProfile.Subject!;

        // Check cache first
        if (_principalCache.TryGetValue(subject, out PrincipalContext? cached) && cached != null)
        {
            // Merge cached DB principal with live token's realm roles
            return new PrincipalContext
            {
                UserId = cached.UserId,
                Email = cached.Email,
                DisplayName = cached.DisplayName,
                AuthProvider = cached.AuthProvider,
                ExternalSubject = cached.ExternalSubject,
                IsSiteAdmin = tokenProfile.IsSiteAdmin
            };
        }

        // Cache miss — query DB
        var principal = await identityLinkService.FindByExternalIdentityAsync(
            AuthProvider.Keycloak,
            subject);

        if (principal == null)
        {
            _logger.LogDebug(
                "Keycloak subject {Subject} not linked to internal user. User needs to bootstrap.",
                subject);

            // Return a partial principal with external info only
            // Don't cache unlinked users — they'll link soon
            return new PrincipalContext
            {
                UserId = Guid.Empty,
                Email = tokenProfile.Email ?? string.Empty,
                DisplayName = tokenProfile.DisplayName,
                AuthProvider = AuthProvider.Keycloak,
                ExternalSubject = subject,
                IsSiteAdmin = tokenProfile.IsSiteAdmin
            };
        }

        // Cache the DB-sourced principal
        _principalCache.Set(subject, principal, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        // Merge DB principal with token's realm roles (IsSiteAdmin comes from token, not DB)
        return new PrincipalContext
        {
            UserId = principal.UserId,
            Email = principal.Email,
            DisplayName = principal.DisplayName,
            AuthProvider = principal.AuthProvider,
            ExternalSubject = principal.ExternalSubject,
            IsSiteAdmin = tokenProfile.IsSiteAdmin
        };
    }

    private PrincipalContext ResolveLegacyPrincipal(ClaimsPrincipal user)
    {
        // Support legacy JWT with ClaimTypes.NameIdentifier or user_id claim
        var userIdClaim = user.FindFirst("user_id")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning("Invalid legacy token - missing or invalid user ID claim");
            return PrincipalContext.Anonymous;
        }

        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;
        var displayName = user.FindFirst(ClaimTypes.Name)?.Value;

        return new PrincipalContext
        {
            UserId = userId,
            Email = email,
            DisplayName = displayName,
            AuthProvider = AuthProvider.Local,
            ExternalSubject = null
        };
    }

    private async Task<AuthorizationContext> ResolveAuthorizationContextAsync(
        IIdentityLinkService identityLinkService,
        IBreakGlassSessionStore breakGlassSessionService,
        Guid userId,
        TenantContext tenant,
        bool isSiteAdmin)
    {
        // Site admins require an active break-glass session to access tenant data.
        // This prevents always-on privileged access; access must be explicitly declared.
        if (isSiteAdmin)
        {
            if (breakGlassSessionService.HasActiveSession(userId, tenant.TenantSlug))
            {
                // Break-glass access is audited once per session at grant/revoke time in the
                // break-glass endpoints (break_glass.granted / .revoked), NOT here — this middleware
                // runs on every request and would otherwise write one audit row per request.
                return new AuthorizationContext
                {
                    TenantId = tenant.TenantId,
                    TenantSlug = tenant.TenantSlug,
                    Role = TenantRole.Admin
                };
            }

            // No active break-glass session — fall through to regular membership
            // lookup. Site admins can be legitimate members of tenants.
            _logger.LogDebug(
                "Site admin {AdminId} has no active break-glass session for tenant {TenantSlug}, checking regular membership",
                userId, tenant.TenantSlug);
        }

        // Check role cache
        var cacheKey = $"{userId}:{tenant.TenantId}";
        if (_roleCache.TryGetValue(cacheKey, out TenantRole cachedRole))
        {
            return new AuthorizationContext
            {
                TenantId = tenant.TenantId,
                TenantSlug = tenant.TenantSlug,
                Role = cachedRole
            };
        }

        var role = await identityLinkService.GetUserTenantRoleAsync(userId, tenant.TenantId);

        // Cache the result
        _roleCache.Set(cacheKey, role, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return new AuthorizationContext
        {
            TenantId = tenant.TenantId,
            TenantSlug = tenant.TenantSlug,
            Role = role
        };
    }
}
