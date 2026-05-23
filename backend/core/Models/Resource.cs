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
}

public record UpdateResourceRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public string? AllocationMode { get; init; }
    public int? BaseAvailabilityPercent { get; init; }
    public bool? IsActive { get; init; }
}

public record ResourceListFilter
{
    public string? ResourceTypeKey { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }
}
