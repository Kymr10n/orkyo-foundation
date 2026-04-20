using Api.Models;

namespace Api.Repositories;

public interface ISpaceGroupRepository
{
    Task<List<SpaceGroupInfo>> GetAllAsync();
    Task<PagedResult<SpaceGroupInfo>> GetAllAsync(PageRequest page);
    Task<SpaceGroupInfo?> GetByIdAsync(Guid groupId);
    Task<SpaceGroupInfo> CreateAsync(string name, string? description, string? color, int displayOrder);
    Task<SpaceGroupInfo?> UpdateAsync(Guid groupId, string? name, string? description, string? color, int? displayOrder);
    Task<bool> DeleteAsync(Guid groupId);
}
