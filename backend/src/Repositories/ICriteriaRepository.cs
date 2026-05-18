using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for criterion definitions. Criteria describe attributes that can be
/// set on resources and required by requests (e.g. "has projector", "load capacity").
/// </summary>
public interface ICriteriaRepository
{
    /// <summary>Returns all criteria, ordered by name.</summary>
    Task<List<CriterionInfo>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns a page of criteria, ordered by name.</summary>
    Task<PagedResult<CriterionInfo>> GetAllAsync(PageRequest page, CancellationToken ct = default);

    /// <summary>Returns all criteria applicable to the given resource type key (e.g. "space", "person").</summary>
    Task<List<CriterionInfo>> GetByResourceTypeAsync(string resourceTypeKey, CancellationToken ct = default);

    /// <summary>Returns the criterion with the given ID, or <c>null</c> if not found.</summary>
    Task<CriterionInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a new criterion. Throws <see cref="Helpers.ConflictException"/> if the name already exists.</summary>
    Task<CriterionInfo> CreateAsync(
        string name,
        string? description,
        CriterionDataType dataType,
        List<string>? enumValues,
        string? unit,
        IReadOnlyList<string> resourceTypeKeys, CancellationToken ct = default);

    /// <summary>
    /// Updates mutable fields on an existing criterion. Returns <c>null</c> if the criterion was not found.
    /// Name and data type are immutable after creation.
    /// </summary>
    Task<CriterionInfo?> UpdateAsync(Guid id, string? description, List<string>? enumValues, string? unit, CancellationToken ct = default);

    /// <summary>Deletes the criterion. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
