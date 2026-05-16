namespace Api.Models;

/// <summary>
/// Information about a resource group (e.g. a people group).
/// </summary>
public record ResourceGroupInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required int DefaultAvailabilityPercent { get; init; }
    public required int MemberCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to create a new resource group.
/// </summary>
public record CreateResourceGroupRequest
{
    public required string ResourceTypeKey { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int DefaultAvailabilityPercent { get; init; } = 100;
}

/// <summary>
/// Request to update an existing resource group. All fields are optional (partial update).
/// </summary>
public record UpdateResourceGroupRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? DefaultAvailabilityPercent { get; init; }
}
