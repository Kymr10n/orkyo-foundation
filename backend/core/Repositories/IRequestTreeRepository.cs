using Api.Models;

namespace Api.Repositories;

/// <summary>
/// The parent/child tree over requests: children, moves, cycle checks and subtree deletes.
/// </summary>
public interface IRequestTreeRepository
{
    /// <summary>Returns direct children of the given parent request.</summary>
    Task<List<RequestInfo>> GetChildrenAsync(Guid parentId, CancellationToken ct = default);

    /// <summary>
    /// Moves a request to a new parent (or to root when <paramref name="newParentId"/> is <c>null</c>).
    /// Returns <c>null</c> if the request was not found.
    /// </summary>
    Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder, CancellationToken ct = default);

    /// <summary>Returns the total count of all descendants (children, grandchildren, etc.).</summary>
    Task<int> GetDescendantCountAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if reparenting <paramref name="requestId"/> to <paramref name="newParentId"/> would create a cycle.</summary>
    Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if the request has at least one direct child.</summary>
    Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes the request and all its descendants in a single transaction. Returns the number of deleted rows.</summary>
    Task<int> DeleteSubtreeAsync(Guid id, CancellationToken ct = default);
}
