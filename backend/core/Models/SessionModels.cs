namespace Api.Models;

// Session models shared between SessionService and SessionEndpoints.

public record CreateAccountRequest
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public string? DisplayName { get; init; }
}

public record SessionBootstrapResponse
{
    public required UserInfo User { get; init; }
    public bool TosRequired { get; init; }
    public string? RequiredTosVersion { get; init; }

    /// <summary>
    /// Terms of Service text to display; populated only when <see cref="TosRequired"/> is true.
    /// Resolved from the site-scoped <c>legal.tos_text</c> setting (compiled default when unset).
    /// </summary>
    public string? TosText { get; init; }
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
