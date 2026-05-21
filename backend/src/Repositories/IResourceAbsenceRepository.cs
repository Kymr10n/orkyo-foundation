using Api.Models;

namespace Api.Repositories;

public interface IResourceAbsenceRepository
{
    Task<List<ResourceAbsenceInfo>> GetByResourceAsync(Guid resourceId, CancellationToken ct = default);
    Task<ResourceAbsenceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceAbsenceInfo> CreateAsync(Guid resourceId, CreateResourceAbsenceRequest request, CancellationToken ct = default);
    Task<ResourceAbsenceInfo?> UpdateAsync(Guid id, UpdateResourceAbsenceRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns all enabled absences for the given set of resources. Used by the availability resolver.</summary>
    Task<Dictionary<Guid, List<ResourceAbsenceInfo>>> GetEnabledByResourcesAsync(IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
}
