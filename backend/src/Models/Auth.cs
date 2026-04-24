namespace Api.Models;

// Control Plane Models

public enum TenantStatus
{
    Active,
    Suspended,
    Deleting
}

public class Tenant
{
    public Guid Id { get; set; }
    public required string Slug { get; set; }
    public required string DisplayName { get; set; }
    public TenantStatus Status { get; set; }
    public required string DbIdentifier { get; set; }
    public ServiceTier Tier { get; set; } = ServiceTier.Free;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Tenant Database Models
//
// User / UserRole / UserStatus models live in orkyo-foundation
// (Api.Models.User) and are consumed via the foundation reference. They are
// shared cross-product because both SaaS and Community have a control-plane
// users table joined to per-tenant memberships.

public class UserIdentity
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderSubject { get; set; }
    public string? ProviderEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class Site
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Code { get; set; }
    public string? Attributes { get; set; } // JSON
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

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


