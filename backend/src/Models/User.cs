namespace Api.Models;

/// <summary>
/// Global lifecycle state of a user account in the control plane.
/// Distinct from per-tenant membership status. Shared across SaaS and Community.
/// </summary>
public enum UserStatus
{
    Active,
    Disabled,
    PendingVerification
}

/// <summary>
/// Per-tenant role of a user as exposed by API responses.
/// Sourced from <c>tenant_memberships.role</c> joined onto the user row.
/// String form lives in <c>RoleConstants</c>; parsing/mapping lives in
/// <c>UserHelper</c>.
/// </summary>
public enum UserRole
{
    Admin,
    Editor,
    Viewer
}

/// <summary>
/// Composite user-with-tenant-membership projection used by user listing /
/// detail endpoints. Identity fields come from <c>users</c>; <see cref="Role"/>
/// and <see cref="IsTenantAdmin"/> come from the joined <c>tenant_memberships</c>
/// row for the active tenant.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public UserStatus Status { get; set; }
    public UserRole Role { get; set; }
    public bool IsTenantAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? KeycloakId { get; set; }
    public string? KeycloakMetadata { get; set; } // JSON
}
