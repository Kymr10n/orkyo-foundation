using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface ICriteriaService
{
    Task<List<CriterionInfo>> GetAllAsync();
    Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page);
    Task<CriterionInfo?> GetByIdAsync(Guid id);
    Task<CriterionInfo> CreateAsync(string name, string? description, CriterionDataType dataType, List<string>? enumValues, string? unit);
    Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit);
    Task<bool> DeleteAsync(Guid id);
}

public class CriteriaService(ICriteriaRepository repository) : ICriteriaService
{
    public Task<List<CriterionInfo>> GetAllAsync() => repository.GetAllAsync();
    public Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page) => repository.GetAllAsync(page);
    public Task<CriterionInfo?> GetByIdAsync(Guid id) => repository.GetByIdAsync(id);
    public Task<CriterionInfo> CreateAsync(string name, string? description, CriterionDataType dataType, List<string>? enumValues, string? unit)
        => repository.CreateAsync(name, description, dataType, enumValues, unit);
    public Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit)
        => repository.UpdateAsync(id, description, enumValues, unit);
    public Task<bool> DeleteAsync(Guid id) => repository.DeleteAsync(id);
}
