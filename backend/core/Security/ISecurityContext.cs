namespace Api.Security;

/// <summary>
/// Provides access to the current authenticated user's identity.
/// Injected into endpoints and services that need user context.
/// </summary>
public interface ICurrentPrincipal
{
    /// <summary>Whether the current request is authenticated</summary>
    bool IsAuthenticated { get; }

    /// <summary>The current user's internal ID</summary>
    Guid UserId { get; }

    /// <summary>The current user's email</summary>
    string Email { get; }

    /// <summary>The current user's display name</summary>
    string? DisplayName { get; }

    /// <summary>Whether this user has the site-admin role (global admin across all tenants)</summary>
    bool IsSiteAdmin { get; }

    /// <summary>Get the full principal context</summary>
    PrincipalContext GetContext();

    /// <summary>Get user ID or throw if not authenticated</summary>
    Guid RequireUserId();

    /// <summary>Get external identity subject (e.g. Keycloak sub) or throw if missing</summary>
    string RequireExternalSubject();
}

/// <summary>
/// Provides access to the current tenant context.
/// Injected into endpoints and services that need tenant context.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>Whether a tenant has been resolved for this request</summary>
    bool HasTenant { get; }

    /// <summary>The current tenant's ID</summary>
    Guid TenantId { get; }

    /// <summary>The current tenant's slug</summary>
    string TenantSlug { get; }

    /// <summary>The tenant database connection string</summary>
    string TenantDbConnectionString { get; }

    /// <summary>Get tenant ID or throw if no tenant</summary>
    Guid RequireTenantId();
}

/// <summary>
/// Provides access to the current authorization context.
/// Combines tenant and role information for authorization decisions.
/// </summary>
public interface IAuthorizationContext
{
    /// <summary>Whether the user is a member of the current tenant</summary>
    bool IsMember { get; }

    /// <summary>The user's role in the current tenant</summary>
    TenantRole Role { get; }

    /// <summary>Whether the user has admin privileges</summary>
    bool IsAdmin { get; }

    /// <summary>Whether the user can edit content</summary>
    bool CanEdit { get; }

    /// <summary>Whether the user can view content</summary>
    bool CanView { get; }

    /// <summary>Get the full authorization context</summary>
    AuthorizationContext GetContext();

    /// <summary>Require the user to be a member, throw 403 if not</summary>
    void RequireMembership();

    /// <summary>Require a specific role, throw 403 if not met</summary>
    void RequireRole(TenantRole minimumRole);
}
