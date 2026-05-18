using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Service layer over criteria definitions. Delegates directly to <see cref="ICriteriaRepository"/>;
/// the interface exists so the HTTP layer depends on a mockable abstraction.
/// </summary>
public interface ICriteriaService
{
    /// <summary>Returns all criteria, ordered by name.</summary>
    Task<List<CriterionInfo>> GetAllAsync(CancellationToken ct = default);
    /// <summary>Returns all criteria applicable to the given resource type key.</summary>
    Task<List<CriterionInfo>> GetByResourceTypeAsync(string resourceTypeKey, CancellationToken ct = default);
    /// <summary>Returns a page of criteria.</summary>
    Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default);
    /// <summary>Returns the criterion with the given ID, or <c>null</c> if not found.</summary>
    Task<CriterionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// <summary>Creates a new criterion.</summary>
    Task<CriterionInfo> CreateAsync(
        string name,
        string? description,
        CriterionDataType dataType,
        List<string>? enumValues,
        string? unit,
        IReadOnlyList<string> resourceTypeKeys, CancellationToken ct = default);
    /// <summary>Updates a criterion's mutable fields. Returns <c>null</c> if not found.</summary>
    Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit, CancellationToken ct = default);
    /// <summary>Deletes a criterion. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public class CriteriaService(ICriteriaRepository repository) : ICriteriaService
{
    public Task<List<CriterionInfo>> GetAllAsync(CancellationToken ct = default) => repository.GetAllAsync(ct);
    public Task<List<CriterionInfo>> GetByResourceTypeAsync(string resourceTypeKey, CancellationToken ct = default) => repository.GetByResourceTypeAsync(resourceTypeKey, ct);
    public Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default) => repository.GetAllAsync(page, ct);
    public Task<CriterionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default) => repository.GetByIdAsync(id, ct);
    public Task<CriterionInfo> CreateAsync(
        string name,
        string? description,
        CriterionDataType dataType,
        List<string>? enumValues,
        string? unit,
        IReadOnlyList<string> resourceTypeKeys, CancellationToken ct = default)
        => repository.CreateAsync(name, description, dataType, enumValues, unit, resourceTypeKeys, ct);
    public Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit, CancellationToken ct = default)
        => repository.UpdateAsync(id, description, enumValues, unit, ct);
    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => repository.DeleteAsync(id, ct);
}
