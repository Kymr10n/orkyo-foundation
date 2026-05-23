using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for the organizational department hierarchy.
/// Departments form a tree; a department may have one parent and many children.
/// </summary>
public interface IDepartmentRepository
{
    /// <summary>Returns all departments, optionally including deactivated ones.</summary>
    Task<List<DepartmentInfo>> GetAllAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>
    /// Returns the department tree, assembled from a single flat query.
    /// Roots are departments without a parent.
    /// </summary>
    Task<List<DepartmentTreeNode>> GetTreeAsync(bool includeInactive = false, CancellationToken ct = default);

    /// <summary>Returns the department with the given ID, or <c>null</c> if not found.</summary>
    Task<DepartmentInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Creates a department. Throws <see cref="Helpers.ConflictException"/> on duplicate sibling name,
    /// <see cref="ArgumentException"/> if the parent does not exist.
    /// </summary>
    Task<DepartmentInfo> CreateAsync(CreateDepartmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Updates a department. Returns <c>null</c> if not found.
    /// Throws <see cref="Helpers.ConflictException"/> on duplicate sibling name or circular reparent.
    /// </summary>
    Task<DepartmentInfo?> UpdateAsync(Guid id, UpdateDepartmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Deletes a department. Returns <c>false</c> if not found.
    /// Throws <see cref="Helpers.ConflictException"/> if the department has children.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
