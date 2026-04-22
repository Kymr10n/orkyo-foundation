namespace Api.Security;

/// <summary>
/// Scoped implementation of ICurrentPrincipal.
/// Populated by ContextEnrichmentMiddleware during request processing.
/// </summary>
public sealed class CurrentPrincipal : ICurrentPrincipal
{
    private PrincipalContext? _context;

    public bool IsAuthenticated => _context?.IsAuthenticated ?? false;

    public Guid UserId => _context?.UserId ?? Guid.Empty;

    public string Email => _context?.Email ?? string.Empty;

    public string? DisplayName => _context?.DisplayName;

    /// <summary>Whether this user has the site-admin role (global admin across all tenants)</summary>
    public bool IsSiteAdmin => _context?.IsSiteAdmin ?? false;

    public PrincipalContext GetContext() => _context ?? PrincipalContext.Anonymous;

    public Guid RequireUserId()
    {
        if (!IsAuthenticated)
            throw new UnauthorizedAccessException("Authentication required");
        return UserId;
    }

    public string RequireExternalSubject()
    {
        var sub = _context?.ExternalSubject;
        if (string.IsNullOrEmpty(sub))
            throw new ArgumentException("User identity not found");
        return sub;
    }

    /// <summary>Set the principal context (called by middleware)</summary>
    public void SetContext(PrincipalContext context) => _context = context;
}

/// <summary>
/// Scoped implementation of IAuthorizationContext.
/// Populated by ContextEnrichmentMiddleware during request processing.
/// </summary>
public sealed class CurrentAuthorizationContext : IAuthorizationContext
{
    private AuthorizationContext? _context;

    public bool IsMember => _context?.IsMember ?? false;

    public TenantRole Role => _context?.Role ?? TenantRole.None;

    public bool IsAdmin => _context?.IsAdmin ?? false;

    public bool CanEdit => _context?.CanEdit ?? false;

    public bool CanView => _context?.CanView ?? false;

    public AuthorizationContext GetContext() => _context ?? throw new InvalidOperationException("Authorization context not set");

    public void RequireMembership()
    {
        if (!IsMember)
            throw new UnauthorizedAccessException("Tenant membership required");
    }

    public void RequireRole(TenantRole minimumRole)
    {
        if (Role < minimumRole)
            throw new UnauthorizedAccessException($"Role {minimumRole} required, but user has {Role}");
    }

    /// <summary>Set the authorization context (called by middleware)</summary>
    public void SetContext(AuthorizationContext context) => _context = context;
}