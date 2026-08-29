using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Reads and writes precedence edges between requests. Edge rows disappear with either
/// endpoint through the FK cascade, so there is no orphan cleanup here.
/// </summary>
public interface IRequestDependencyRepository
{
    /// <summary>
    /// Every edge in the org, optionally narrowed to edges whose successor sits at a site.
    /// The tree view, the Gantt export and the critical path all need the whole set at once;
    /// fetching per node would be an N+1 over the hierarchy.
    /// </summary>
    Task<List<RequestDependencyInfo>> GetAllAsync(Guid? siteId, CancellationToken ct = default);

    /// <summary>Edges pointing into and out of one request.</summary>
    Task<RequestDependencies> GetForRequestAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Edges whose successor is any of <paramref name="successorIds"/>. One read for a batch —
    /// conflict detection runs over many requests and must not query per request.
    /// </summary>
    Task<List<RequestDependencyInfo>> GetBySuccessorsAsync(
        IReadOnlyCollection<Guid> successorIds, CancellationToken ct = default);

    Task<RequestDependencyInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<RequestDependencyInfo> CreateAsync(
        Guid predecessorId, Guid successorId, string dependencyType, int lagMinutes,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>True when the pair already has an edge, in that direction.</summary>
    Task<bool> ExistsAsync(Guid predecessorId, Guid successorId, CancellationToken ct = default);

    /// <summary>
    /// True when adding predecessor → successor would close a loop, i.e. the proposed
    /// predecessor is already reachable by following edges forward from the successor.
    /// </summary>
    Task<bool> WouldCreateCycleAsync(Guid predecessorId, Guid successorId, CancellationToken ct = default);

    /// <summary>True when the request is an endpoint of at least one edge.</summary>
    Task<bool> HasAnyForRequestAsync(Guid requestId, CancellationToken ct = default);
}
