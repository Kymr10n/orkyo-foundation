namespace Api.Models;

/// <summary>
/// Tenant-scoped reference data: a flat list of job titles that can be assigned
/// to person resources via <see cref="PersonProfileInfo.JobTitleId"/>.
/// </summary>
public record JobTitleInfo
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateJobTitleRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public record UpdateJobTitleRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsActive { get; init; }
}
