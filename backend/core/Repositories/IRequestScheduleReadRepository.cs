using Api.Models;

namespace Api.Repositories;

/// <summary>
/// The scheduled / unscheduled read surface over requests — the feeds behind the scheduler
/// grid, the conflicts registry and the auto-scheduler's eligibility set. Reads only; the
/// schedule writes stay on <see cref="IRequestRepository"/>.
/// </summary>
public interface IRequestScheduleReadRepository
{
    /// <summary>Returns all scheduled (assigned to a space) requests for the given site.</summary>
    Task<List<RequestInfo>> GetScheduledBySiteAsync(Guid siteId, CancellationToken ct = default);

    /// <summary>All scheduled requests tenant-wide (have a space assignment + start_ts), requirements
    /// hydrated — the authoritative input for the all-time conflicts registry.</summary>
    Task<List<RequestInfo>> GetScheduledAsync(CancellationToken ct = default);

    /// <summary>Scheduled requests tenant-wide whose bar overlaps [from,to] — the windowed conflicts
    /// feed for the utilization grid (all sites, scoped to the visible window).</summary>
    Task<List<RequestInfo>> GetScheduledAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Same row set as <see cref="GetScheduledAsync(DateTime, DateTime, CancellationToken)"/>
    /// as a lightweight (id, start_ts, site_id) projection — no assignments view, no requirements.</summary>
    Task<List<ScheduledRequestLite>> GetScheduledLiteAsync(DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Scheduled requests for one site whose bar overlaps [from,to] — the scoped grid feed.</summary>
    Task<List<RequestInfo>> GetScheduledBySiteWindowAsync(Guid siteId, DateTime from, DateTime to, CancellationToken ct = default);

    /// <summary>Unscheduled, directly-schedulable (leaf) requests tenant-wide — the drag-to-schedule backlog. Groups are excluded; their null start_ts is derived, not unscheduled.</summary>
    Task<List<RequestInfo>> GetUnscheduledAsync(Guid? siteId = null, bool includeSiteNeutral = true, bool includeRequirements = false, CancellationToken ct = default);

    /// <summary>Partially-scheduled leaf requests tenant-wide: they have a <c>start_ts</c> but are not
    /// fully scheduled (no <c>end_ts</c>, or no non-cancelled Space assignment) — i.e. <see cref="RequestInfo.IsScheduled"/>
    /// is false. Complements <see cref="GetUnscheduledAsync"/> (which requires <c>start_ts IS NULL</c>) so
    /// the auto-scheduler still sees timed-but-spaceless leaves that were eligible before.</summary>
    Task<List<RequestInfo>> GetPartiallyScheduledLeavesAsync(bool includeRequirements = false, CancellationToken ct = default);
}
