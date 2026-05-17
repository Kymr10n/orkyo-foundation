using Api.Models;

namespace Api.Repositories;

public interface ICriteriaRepository
{
    Task<List<CriterionInfo>> GetAllAsync();
    Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page);
    Task<List<CriterionInfo>> GetByResourceTypeAsync(string resourceTypeKey);
    Task<CriterionInfo?> GetByIdAsync(Guid id);
    Task<CriterionInfo> CreateAsync(
        string name,
        string? description,
        CriterionDataType dataType,
        List<string>? enumValues,
        string? unit,
        IReadOnlyList<string> resourceTypeKeys);
    Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit);
    Task<bool> DeleteAsync(Guid id);
}
