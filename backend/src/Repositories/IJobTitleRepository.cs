using Api.Models;

namespace Api.Repositories;

/// <summary>Persistence layer for job title definitions used to tag person resources.</summary>
public interface IJobTitleRepository
{
    /// <summary>Returns all job titles, optionally including deactivated ones.</summary>
    Task<List<JobTitleInfo>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>Returns the job title with the given ID, or <c>null</c> if not found.</summary>
    Task<JobTitleInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a new job title. Throws <see cref="Helpers.ConflictException"/> on duplicate name.</summary>
    Task<JobTitleInfo> CreateAsync(CreateJobTitleRequest request, CancellationToken ct = default);

    /// <summary>Updates a job title. Returns <c>null</c> if not found.</summary>
    Task<JobTitleInfo?> UpdateAsync(Guid id, UpdateJobTitleRequest request, CancellationToken ct = default);

    /// <summary>Deletes a job title. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
