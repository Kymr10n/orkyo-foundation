namespace Api.Models;

/// <summary>
/// Person profile information (extends resource for type='person').
///
/// Job title and department left this record in migration 1820: they are organization lists now,
/// carried on the resource as <c>list_lookup</c> custom fields like any other list. What remains
/// is what genuinely belongs to the person — contact address, linked account, private notes.
/// </summary>
public record PersonProfileInfo
{
    public required Guid ResourceId { get; init; }
    public string? Email { get; init; }
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
    public string? Notes { get; init; }
}

/// <summary>
/// Request to link a user to a person profile.
/// </summary>
public record LinkUserToPersonProfileRequest
{
    public required Guid UserId { get; init; }
}
