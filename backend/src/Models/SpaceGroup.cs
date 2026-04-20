using Api.Constants;

namespace Api.Models;

/// <summary>
/// Shared interface for space group requests that carry optional Color and Description fields.
/// Used by the shared validator base to avoid duplicating those rules.
/// </summary>
public interface ISpaceGroupRequest
{
    string? Color { get; }
    string? Description { get; }
}

/// <summary>
/// Space group information.
/// Groups are tenant-wide and can contain spaces from multiple sites.
/// </summary>
public record SpaceGroupInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; } // Hex color (#RRGGBB)
    public required int DisplayOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int? SpaceCount { get; init; } // Optional: number of spaces in this group
}

/// <summary>
/// Request to create a new space group.
/// </summary>
public record CreateSpaceGroupRequest : ISpaceGroupRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }
    public int DisplayOrder { get; init; } = 0;
}

/// <summary>
/// Request to update an existing space group.
/// All fields are optional (partial update).
/// </summary>
public record UpdateSpaceGroupRequest : ISpaceGroupRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }
    public int? DisplayOrder { get; init; }
}
