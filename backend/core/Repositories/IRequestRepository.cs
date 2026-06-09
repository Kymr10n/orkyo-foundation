using Api.Models;

namespace Api.Repositories;

/// <summary>
/// Persistence layer for requests — the scheduling units of the system.
/// Requests form a tree (via parent/child relationships) and carry requirements
/// that are matched against resource capabilities during auto-scheduling.
/// </summary>
public interface IRequestRepository
{
    /// <summary>Returns all requests. Pass <c>includeRequirements: true</c> to populate the requirements list.</summary>
    Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false, CancellationToken ct = default);

    /// <summary>Returns a page of requests.</summary>
    Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false, CancellationToken ct = default);

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

    /// <summary>Returns all scheduled (assigned to a space) requests for the given site.</summary>
    Task<List<RequestInfo>> GetScheduledBySiteAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>All scheduled requests tenant-wide (have a space assignment + start_ts), requirements
    /// hydrated — the authoritative input for the conflicts registry.</summary>
    Task<List<RequestInfo>> GetScheduledAsync(CancellationToken ct = default);

    /// <summary>Scheduled requests for one site whose bar overlaps [from,to] — the scoped grid feed.</summary>
    Task<List<RequestInfo>> GetScheduledBySiteWindowAsync(Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Unscheduled requests (no start_ts) tenant-wide — the drag-to-schedule backlog for the panel.</summary>
    Task<List<RequestInfo>> GetUnscheduledAsync(CancellationToken ct = default);

    /// <summary>Updates the schedule (space, start, end) of a request. Returns <c>null</c> if not found.</summary>
    Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request, CancellationToken ct = default);

    /// <summary>Applies schedule updates to a batch of requests in a single transaction.</summary>
    Task<int> BatchUpdateSchedulesAsync(IReadOnlyList<(Guid Id, ScheduleRequestRequest Data)> updates, CancellationToken ct = default);

    // ── Requirements ────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a requirement to a request. Throws <see cref="Helpers.NotFoundException"/> if the request
    /// or criterion does not exist, <see cref="ArgumentException"/> if the criterion is not applicable
    /// to requests.
    /// </summary>
    Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement, CancellationToken ct = default);

    /// <summary>Removes a requirement. Returns <c>false</c> if not found.</summary>
    Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId, CancellationToken ct = default);

    // ── Tree ────────────────────────────────────────────────────────────────

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

    /// <summary>Returns the planning mode of the request, or <c>null</c> if not found.</summary>
    Task<PlanningMode?> GetPlanningModeAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if the request has at least one direct child.</summary>
    Task<bool> HasChildrenAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes the request and all its descendants in a single transaction. Returns the number of deleted rows.</summary>
    Task<int> DeleteSubtreeAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Returns all active requests whose scheduled window overlaps [<paramref name="start"/>, <paramref name="end"/>).
    /// Each tuple carries the <see cref="RequestInfo"/> and the ID of the resource's non-cancelled assignment
    /// on that request (null when not yet assigned). Requirements are populated for capability-match computation.
    /// </summary>
    Task<List<(RequestInfo Request, Guid? AssignmentId)>> GetCandidatesOverlappingAsync(Guid resourceId, DateTime start, DateTime end, CancellationToken ct = default);
}
