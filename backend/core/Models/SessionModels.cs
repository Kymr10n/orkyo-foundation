namespace Api.Models;

// Session response models shared between SessionService and SessionEndpoints.

public record SessionBootstrapResponse
{
    public required UserInfo User { get; init; }
    public bool TosRequired { get; init; }
    public string? RequiredTosVersion { get; init; }
    public List<TenantMembershipInfo> Tenants { get; init; } = new();
    public string? SuggestedTenantSlug { get; init; }
    public bool IsSiteAdmin { get; init; }
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
    public bool IsTenantAdmin { get; init; }
    public required string Tier { get; init; }
}
