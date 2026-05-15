namespace Api.Models;

/// <summary>
/// Person profile information (extends resource for type='person').
/// </summary>
public record PersonProfileInfo
{
    public required Guid ResourceId { get; init; }
    public string? Email { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public Guid? LinkedUserId { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Request to upsert a person profile.
/// </summary>
public record UpsertPersonProfileRequest
{
    public string? Email { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Request to link a user to a person profile.
/// </summary>
public record LinkUserToPersonProfileRequest
{
    public required Guid UserId { get; init; }
}
