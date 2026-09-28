using Api.Models;

namespace Api.Repositories;

public interface IResourceAbsenceRepository
{
    Task<List<ResourceAbsenceInfo>> GetByResourceAsync(Guid resourceId, CancellationToken ct = default);
    /// <summary>Throws <see cref="Helpers.NotFoundException"/> when the resource does not exist.</summary>
    Task<ResourceAbsenceInfo> CreateAsync(Guid resourceId, CreateResourceAbsenceRequest request, CancellationToken ct = default);

    // Update and delete take the resource the caller addressed: an absence of another resource is
    // not found (null / false), so the parent-scope rule lives here rather than in each caller.
    Task<ResourceAbsenceInfo?> UpdateAsync(Guid resourceId, Guid id, UpdateResourceAbsenceRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid resourceId, Guid id, CancellationToken ct = default);

    /// <summary>Returns all enabled absences for the given set of resources. Used by the availability resolver.</summary>
    Task<Dictionary<Guid, List<ResourceAbsenceInfo>>> GetEnabledByResourcesAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
}
