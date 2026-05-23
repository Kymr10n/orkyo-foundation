namespace Api.Security;

/// <summary>
/// User role within a tenant.
/// Roles are tenant-specific - a user can have different roles in different tenants.
/// </summary>
public enum TenantRole
{
    /// <summary>No role / not a member</summary>
    None,

    /// <summary>Read-only access</summary>
    Viewer,

    /// <summary>Can create and edit content</summary>
    Editor,

    /// <summary>Full tenant administration</summary>
    Admin
}

/// <summary>
/// Combined authorization context containing tenant and role information.
/// Used for authorization decisions throughout the application.
/// </summary>
public sealed class AuthorizationContext
{
    /// <summary>Tenant ID</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Tenant slug (for routing/display)</summary>
    public required string TenantSlug { get; init; }

    /// <summary>User's role in this tenant</summary>
    public required TenantRole Role { get; init; }

    /// <summary>Whether the user is a member of the tenant</summary>
    public bool IsMember => Role != TenantRole.None;

    /// <summary>Whether the user has admin privileges</summary>
    public bool IsAdmin => Role == TenantRole.Admin;

    /// <summary>Whether the user can edit content</summary>
    public bool CanEdit => Role >= TenantRole.Editor;

    /// <summary>Whether the user can view content</summary>
    public bool CanView => Role >= TenantRole.Viewer;

    /// <summary>Creates a context for a non-member</summary>
    public static AuthorizationContext NoAccess(Guid tenantId, string tenantSlug) => new()
    {
        TenantId = tenantId,
        TenantSlug = tenantSlug,
        Role = TenantRole.None
    };

    /// <summary>Role as string (for compatibility with existing code)</summary>
    public string RoleString => Role.ToString().ToLowerInvariant();
}
