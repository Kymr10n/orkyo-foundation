namespace Api.Models;

// Control Plane Models

public enum TenantStatus
{
    Active,
    Suspended,
    Deleting
}

// Tenant Database Models
//
// User / UserRole / UserStatus models live in orkyo-foundation
// (Api.Models.User) and are consumed via the foundation reference. They are
// shared cross-product because both SaaS and Community have a control-plane
// users table joined to per-tenant memberships.

public class Invitation
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public UserRole Role { get; set; }
    public Guid InvitedBy { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
