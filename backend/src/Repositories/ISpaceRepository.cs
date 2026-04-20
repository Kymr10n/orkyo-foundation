using Api.Models;

namespace Api.Repositories;

public interface ISpaceRepository
{
    Task<List<SpaceInfo>> GetAllAsync(Guid siteId);
    Task<PagedResult<SpaceInfo>> GetAllAsync(Guid siteId, PageRequest page);
    Task<SpaceInfo?> GetByIdAsync(Guid siteId, Guid spaceId);
    Task<int> GetEstimatedCountAsync();
    Task<SpaceInfo> CreateAsync(Guid siteId, string name, string? code, string? description, bool isPhysical, SpaceGeometry? geometry, Dictionary<string, object>? properties, int capacity = 1);
    Task<SpaceInfo?> UpdateAsync(Guid siteId, Guid spaceId, string? name, string? code, string? description, SpaceGeometry? geometry, Dictionary<string, object>? properties, Guid? groupId = null, int? capacity = null);
    Task<bool> DeleteAsync(Guid siteId, Guid spaceId);
}
