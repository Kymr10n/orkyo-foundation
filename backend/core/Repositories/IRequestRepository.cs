using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for requests — the scheduling units of the system. Covers the CRUD
/// surface, the schedule writes, the overlap search and the requirements.
/// </summary>
/// <remarks>
/// Two sibling repositories exist and each earns it, measured rather than assumed:
/// <see cref="IRequestScheduleReadRepository"/> has eight production consumers and is mocked in
/// eight test classes, so it is a real seam; <see cref="IRequestTreeRepository"/> has two
/// (<c>RequestService</c> and <c>RequestPlanService</c>), and the second needs six tree methods
/// rather than this interface's thirty-odd, which is the narrowing that justifies it.
///
/// A third, for requirements, was folded back in: one implementation, one pure-delegating caller,
/// no test double. A service holding two of these costs two small objects and no extra database
/// connections — each method opens and disposes its own through the factory.
/// </remarks>
public interface IRequestRepository
{
    /// <summary>Returns all requests. Pass <c>includeRequirements: true</c> to populate the requirements list.</summary>
    Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false, Guid? siteId = null, CancellationToken ct = default);

    /// <summary>Returns a page of requests.</summary>
    Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false, CancellationToken ct = default);

    /// <summary>
    /// Name/scheduled filtering applied in SQL with a row cap, for callers that want a few
    /// matches rather than the whole table.
    /// </summary>
    Task<List<RequestInfo>> SearchAsync(string? nameContains, bool? scheduled, int limit, RequestSort sort = RequestSort.Default, CancellationToken ct = default);

    /// <summary>Returns the request with the given ID, or <c>null</c> if not found.</summary>
    Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true, CancellationToken ct = default);

    /// <summary>Bulk fetch by ids (one query; requirements hydrated in one more) — for batch validation.</summary>
    Task<List<RequestInfo>> GetByIdsAsync(IReadOnlyList<Guid> ids, bool includeRequirements = true, CancellationToken ct = default);

    /// <summary>Creates a new request.</summary>
    Task<RequestInfo> CreateAsync(CreateRequestRequest request, CancellationToken ct = default);

    /// <summary>Updates an existing request. Returns <c>null</c> if not found.</summary>
    Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request, CancellationToken ct = default);

    /// <summary>Deletes a request. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if a request with the given ID exists.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    // ── Scheduling ───────────────────────────────────────────────────────────

    /// <summary>Updates the schedule (space, start, end) of a request. Returns <c>null</c> if not found.</summary>
    Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request, CancellationToken ct = default);

    /// <summary>Applies schedule updates to a batch of requests in a single transaction.</summary>
    Task<int> BatchUpdateSchedulesAsync(IReadOnlyList<(Guid Id, ScheduleRequestRequest Data)> updates, CancellationToken ct = default);

    /// <summary>
    /// Writes auto-scheduled placements: each request's window and one resource per targeted
    /// type, in one transaction. Returns the number of requests updated.
    /// </summary>
    Task<int> BatchApplyPlacementsAsync(IReadOnlyList<PlacementWrite> placements, CancellationToken ct = default);

    /// <summary>
    /// Creates a parent with its children and the finish-to-start edges between them in one
    /// transaction — nothing is written unless everything is. Edges name children by index.
    /// </summary>
    Task<(RequestInfo Parent, IReadOnlyList<Guid> ChildIds)> CreateChainAsync(
        CreateRequestRequest parent,
        IReadOnlyList<CreateRequestRequest> children,
        IReadOnlyList<ChainEdge> edges,
        CancellationToken ct = default);

    // ── Stored fields ───────────────────────────────────────────────────────

    /// <summary>Returns the planning mode of the request, or <c>null</c> if not found.</summary>
    Task<PlanningMode?> GetPlanningModeAsync(Guid id, CancellationToken ct = default);

    /// <summary>The STORED status (not the schedule-derived effective one), for transition checks.</summary>
    Task<RequestStatus?> GetStoredStatusAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Stored statuses for several requests at once. The join gate needs these because
    /// <see cref="RequestInfo.Status"/> is already schedule-derived, and that derivation loses a
    /// manual <c>done</c> on a request that was never scheduled.
    /// </summary>
    Task<Dictionary<Guid, RequestStatus>> GetStoredStatusesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Returns all active requests whose scheduled window overlaps [<paramref name="start"/>, <paramref name="end"/>).
    /// Each tuple carries the <see cref="RequestInfo"/> and the ID of the resource's non-cancelled assignment
    /// on that request (null when not yet assigned). Requirements are populated for capability-match computation.
    /// </summary>
    Task<List<(RequestInfo Request, Guid? AssignmentId)>> GetCandidatesOverlappingAsync(Guid resourceId, DateTime start, DateTime end, CancellationToken ct = default);

    // ── Requirements ─────────────────────────────────────────────────────────
    // Kept on this interface rather than a separate IRequestRequirementRepository: that
    // interface had one implementation, one caller that pure-delegated, and no test double.

    /// <summary>
    /// Adds a requirement to a request. Throws <see cref="Helpers.NotFoundException"/> if the request
    /// or criterion does not exist, <see cref="ArgumentException"/> if the criterion is not applicable
    /// to requests.
    /// </summary>
    Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement, CancellationToken ct = default);

    /// <summary>Removes a requirement. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId, CancellationToken ct = default);
}
