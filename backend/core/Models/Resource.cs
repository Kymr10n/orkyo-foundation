namespace Api.Models;

public record ResourceTypeInfo
{
    public required Guid Id { get; init; }
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public required bool IsSystem { get; init; }
    public required bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record ResourceInfo
{
    public required Guid Id { get; init; }
    public required Guid ResourceTypeId { get; init; }
    public required string ResourceTypeKey { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public required string AllocationMode { get; init; }
    public required int BaseAvailabilityPercent { get; init; }
    public required bool IsActive { get; init; }

    // Location model. Spaces resolve their site via spaces.site_id and leave HomeSiteId null;
    // people/tools carry an administrative home site. CurrentSiteId is derived (read-only), not
    // stored: it is where the resource actually is right now.
    /// <summary>Administrative/owning site and idle-time anchor (null for spaces and un-remediated resources).</summary>
    public Guid? HomeSiteId { get; init; }
    /// <summary>Derived (read-only): where the resource is right now — the site of the non-cancelled
    /// assignment overlapping the current time, else the home site (spaces always resolve to their own site).</summary>
    public Guid? CurrentSiteId { get; init; }
    /// <summary>Whether the resource may be assigned to requests at another site.</summary>
    public bool CrossSiteAllowed { get; init; } = true;

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateResourceRequest
{
    public required string ResourceTypeKey { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public required string AllocationMode { get; init; }
    public int BaseAvailabilityPercent { get; init; } = 100;

    public Guid? HomeSiteId { get; init; }
    public bool CrossSiteAllowed { get; init; } = true;
}

public record UpdateResourceRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public string? AllocationMode { get; init; }
    public int? BaseAvailabilityPercent { get; init; }
    public bool? IsActive { get; init; }

    public Guid? HomeSiteId { get; init; }
    public bool? CrossSiteAllowed { get; init; }
}

public record ResourceListFilter
{
    public string? ResourceTypeKey { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }
}
