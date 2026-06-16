namespace Api.Models;

/// <summary>
/// Person profile information (extends resource for type='person').
///
/// Reference data (job title, department) is referenced by ID. The read response
/// also carries resolved <see cref="JobTitleName"/> and <see cref="DepartmentPath"/>
/// fields populated via JOIN — these are read-only and never round-tripped on writes.
/// </summary>
public record PersonProfileInfo
{
    public required Guid ResourceId { get; init; }
    public string? Email { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? DepartmentId { get; init; }
    public Guid? LinkedUserId { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    // Resolved on read via LEFT JOIN to job_titles and a recursive CTE that
    // builds the parent path for departments. Both are display-only.
    public string? JobTitleName { get; init; }
    public string? DepartmentPath { get; init; }
}

/// <summary>
/// Lightweight per-person job-title projection for the utilization grid's label cells.
/// Avoids hydrating the full <see cref="PersonProfileInfo"/> (recursive department CTE +
/// note decryption) when only the job title is rendered.
/// </summary>
public record PersonJobTitleInfo
{
    public required Guid ResourceId { get; init; }
    public string? JobTitleName { get; init; }
}

/// <summary>
/// Request to upsert a person profile. Reference data is set via ID.
/// </summary>
public record UpsertPersonProfileRequest
{
    public string? Email { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Request to link a user to a person profile.
/// </summary>
public record LinkUserToPersonProfileRequest
{
    public required Guid UserId { get; init; }
}
