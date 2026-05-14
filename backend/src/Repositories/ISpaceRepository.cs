using Api.Models;

namespace Api.Repositories;

public interface ISpaceRepository
{
    Task<List<SpaceInfo>> GetAllAsync(Guid siteId);
    Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page);
    Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid resourceId);
    Task<int> GetEstimatedCountAsync();
    Task<SpaceInfo> CreateAsync(Guid resourceId, Guid siteId, string? code, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1);
    Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid resourceId, string? code, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null);
    Task<bool> DeleteAsync(Guid siteId, Guid resourceId);
}
