namespace Api.Models.Admin;

public record AdminUserSummary
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required string Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public required int MembershipCount { get; init; }
    public required int IdentityCount { get; init; }
    public required bool IsSiteAdmin { get; init; }
    public Guid? OwnedTenantId { get; init; }
    public string? OwnedTenantTier { get; init; }
}

public record AdminUserDetail
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public string? DisplayName { get; init; }
    public required string Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public required bool IsSiteAdmin { get; init; }
    public Guid? OwnedTenantId { get; init; }
    public string? OwnedTenantTier { get; init; }
    public required List<AdminUserIdentity> Identities { get; init; }
    public required List<AdminUserMembership> Memberships { get; init; }
}

public record AdminUserIdentity
{
    public required Guid Id { get; init; }
    public required string Provider { get; init; }
    public required string ProviderSubject { get; init; }
    public string? ProviderEmail { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public record AdminUserMembership
{
    public required Guid TenantId { get; init; }
    public required string TenantSlug { get; init; }
    public required string TenantName { get; init; }
    public required string Role { get; init; }
    public required string Status { get; init; }
    public required DateTime JoinedAt { get; init; }
}
