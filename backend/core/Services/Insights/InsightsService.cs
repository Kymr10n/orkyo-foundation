using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Models.Insights;
using Api.Repositories;
using Npgsql;

namespace Api.Services.Insights;

/// <summary>
/// Built-in Insights dashboard semantic layer — the stable seam between operational data and the
/// in-app dashboard / API. Request counts come from the <c>analytics_request_summary_v</c> projection;
/// conflict and utilization analytics reuse <see cref="IConflictService"/> and
/// <see cref="IUtilizationService"/> rather than reimplementing that business logic in SQL. All series
/// share one calendar-aligned bucketing strategy (<see cref="InsightsBuckets"/>).
///
/// The request dimension is anchored on scheduled date (<c>start_ts</c>): time-bound work is scoped
/// and bucketed by when it happens, while backlog (no <c>start_ts</c>) is a timeless count. Tenant
/// scoping is implicit (per-request org/tenant connection); site-neutral requests (no site) are
/// schedulable anywhere and therefore counted under every site.
/// </summary>
public interface IInsightsService
{
    Task<InsightsOverview> GetOverviewAsync(InsightsFilter filter, CancellationToken ct = default);
    Task<InsightsUtilization> GetUtilizationTrendAsync(InsightsFilter filter, CancellationToken ct = default);
    Task<InsightsConflicts> GetConflictTrendAsync(InsightsFilter filter, CancellationToken ct = default);
    Task<InsightsRequests> GetRequestTrendAsync(InsightsFilter filter, CancellationToken ct = default);
}

public class InsightsService(
    OrgContext orgContext,
    IOrgDbConnectionFactory connectionFactory,
    IConflictService conflictService,
    IRequestRepository requestRepository,
    IResourceRepository resourceRepository,
    IResourceAssignmentRepository assignmentRepository,
    IAvailabilityResolver availabilityResolver) : IInsightsService
{
    // The in-app dashboard reports "live": request facts come from the live view and conflict/
    // utilization from live services. This is the swap point — a snapshot-backed view would carry
    // source_mode='snapshot', read here, without changing the API or UI.
    private const string SourceMode = "live";

    // Site-neutral requests (site_id NULL) are schedulable anywhere → counted under any site.
    private const string SiteFilter = "(@siteId::uuid IS NULL OR site_id = @siteId OR site_id IS NULL)";

    public async Task<InsightsOverview> GetOverviewAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        var inWindow = await FetchInWindowFactsAsync(filter.From, filter.To, filter.SiteId, ct);
        var backlog = await FetchBacklogCountAsync(filter.SiteId, ct);
        var conflicts = await LoadConflictTimelineAsync(filter.From, filter.To, filter.SiteId, ct);

        return new InsightsOverview
        {
            Period = new InsightsPeriod { From = filter.From, To = filter.To },
            SiteId = filter.SiteId,
            Requests = CountRequests(inWindow, backlog),
            Conflicts = CountConflicts(conflicts.Select(c => c.Kind)),
            Utilization = new UtilizationSummary
            {
                SpacesPercent = AggregatePercent(await ComputeUtilizationSeriesAsync(
                    ResourceTypeKeys.Space, filter.From, filter.To, "month", filter.SiteId, ct)),
                PeoplePercent = AggregatePercent(await ComputeUtilizationSeriesAsync(
                    ResourceTypeKeys.Person, filter.From, filter.To, "month", filter.SiteId, ct)),
                ToolsPercent = AggregatePercent(await ComputeUtilizationSeriesAsync(
                    ResourceTypeKeys.Tool, filter.From, filter.To, "month", filter.SiteId, ct)),
            },
            Metadata = Metadata(),
        };
    }

    public async Task<InsightsRequests> GetRequestTrendAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        var bucket = filter.Bucket!;
        var buckets = InsightsBuckets.Generate(filter.From, filter.To, bucket);
        var (rangeFrom, rangeTo) = Bounds(buckets, filter);
        // Anchored on scheduled date: only time-bound work appears in the trend. Backlog (no start_ts)
        // is timeless → it lives in the overview Unscheduled KPI, not here.
        var facts = await FetchInWindowFactsAsync(rangeFrom, rangeTo, filter.SiteId, ct);

        var now = DateTime.UtcNow;
        var series = buckets.Select(b =>
        {
            var inBucket = facts.Where(f => f.StartTs >= b.Start && f.StartTs < b.End).ToList();
            // Count by EFFECTIVE status (derived from schedule vs now), matching the read model:
            // a scheduled request is in_progress while running and done once its window has passed.
            var effective = inBucket
                .Select(f => RequestStatusCalculator.Effective(
                    EnumMapper.FromDbValue<RequestStatus>(f.Status), f.StartTs, f.EndTs, now))
                .ToList();
            return new RequestSeriesPoint
            {
                BucketStart = b.Start,
                BucketEnd = b.End,
                Total = inBucket.Count,
                New = effective.Count(s => s == RequestStatus.New),
                InProgress = effective.Count(s => s == RequestStatus.InProgress),
                Done = effective.Count(s => s == RequestStatus.Done),
                Deferred = effective.Count(s => s == RequestStatus.Deferred),
                Cancelled = effective.Count(s => s == RequestStatus.Cancelled),
            };
        }).ToList();

        return new InsightsRequests { Bucket = bucket, Series = series, Metadata = Metadata() };
    }

    public async Task<InsightsConflicts> GetConflictTrendAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        var bucket = filter.Bucket!;
        var buckets = InsightsBuckets.Generate(filter.From, filter.To, bucket);
        var (rangeFrom, rangeTo) = Bounds(buckets, filter);
        var timeline = await LoadConflictTimelineAsync(rangeFrom, rangeTo, filter.SiteId, ct);

        var series = buckets.Select(b =>
        {
            var kinds = timeline.Where(c => c.StartTs >= b.Start && c.StartTs < b.End).Select(c => c.Kind);
            var counts = CountConflicts(kinds);
            return new ConflictSeriesPoint
            {
                BucketStart = b.Start,
                BucketEnd = b.End,
                Total = counts.Total,
                Overbooking = counts.Overbooking,
                CriteriaMismatch = counts.CriteriaMismatch,
                ResourceUnavailable = counts.ResourceUnavailable,
                ScheduleOutsideAvailability = counts.ScheduleOutsideAvailability,
                MissingResource = counts.MissingResource,
            };
        }).ToList();

        return new InsightsConflicts { Bucket = bucket, Series = series, Metadata = Metadata() };
    }

    public async Task<InsightsUtilization> GetUtilizationTrendAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        var bucket = filter.Bucket!;
        var resourceType = filter.ResourceType!;
        var series = await ComputeUtilizationSeriesAsync(resourceType, filter.From, filter.To, bucket, filter.SiteId, ct);
        var rangeFrom = series.Count > 0 ? series[0].Start : filter.From;
        var rangeTo = series.Count > 0 ? series[^1].End : filter.To;
        var timeline = await LoadConflictTimelineAsync(rangeFrom, rangeTo, filter.SiteId, ct);

        var points = series.Select(s =>
        {
            var totalMin = (long)Math.Round(s.CapMinutes);
            var usedMin = (long)Math.Round(s.UsedMinutes);
            return new UtilizationSeriesPoint
            {
                BucketStart = s.Start,
                BucketEnd = s.End,
                TotalCapacityMinutes = totalMin,
                UsedCapacityMinutes = usedMin,
                AvailableCapacityMinutes = Math.Max(totalMin - usedMin, 0),
                UtilizationPercent = s.CapMinutes > 0 ? Math.Round((decimal)(s.UsedMinutes / s.CapMinutes * 100.0), 2) : null,
                ConflictCount = timeline.Count(c => c.StartTs >= s.Start && c.StartTs < s.End),
            };
        }).ToList();

        return new InsightsUtilization
        {
            ResourceType = resourceType,
            Bucket = bucket,
            Series = points,
            Metadata = Metadata(),
        };
    }

    // ── Request facts (from the analytics view, anchored on start_ts) ──────────

    private sealed record RequestFact(string Status, bool IsScheduled, DateTime StartTs, DateTime? EndTs);

    /// <summary>Time-bound requests whose scheduled window starts in [from, to). Site-neutral included.</summary>
    private async Task<List<RequestFact>> FetchInWindowFactsAsync(
        DateTime from, DateTime to, Guid? siteId, CancellationToken ct)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT status, is_scheduled, start_ts, end_ts
            FROM analytics_request_summary_v
            WHERE start_ts >= @from AND start_ts < @to
              AND {SiteFilter}", db);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.AddWithValue("siteId", (object?)siteId ?? DBNull.Value);

        var facts = new List<RequestFact>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            facts.Add(new RequestFact(
                reader.GetString(0), reader.GetBoolean(1), reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3)));
        return facts;
    }

    /// <summary>
    /// Current backlog size: leaf requests with no scheduled window (excludes summary/container
    /// parents, which also lack a start_ts but are not schedulable work). Site-neutral included.
    /// Cancelled requests are not backlog.
    /// </summary>
    private async Task<int> FetchBacklogCountAsync(Guid? siteId, CancellationToken ct)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand($@"
            SELECT COUNT(*)
            FROM analytics_request_summary_v
            WHERE start_ts IS NULL
              AND planning_mode = '{PlanningModes.Leaf}'
              AND status <> '{RequestStatuses.Cancelled}'
              AND {SiteFilter}", db);
        cmd.Parameters.AddWithValue("siteId", (object?)siteId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    private static RequestCounts CountRequests(IReadOnlyCollection<RequestFact> inWindow, int backlog) => new()
    {
        Total = inWindow.Count + backlog,
        Scheduled = inWindow.Count(f => f.IsScheduled && f.Status != RequestStatuses.Cancelled),
        Unscheduled = backlog,
        Completed = inWindow.Count(f => f.Status == RequestStatuses.Done),
        Cancelled = inWindow.Count(f => f.Status == RequestStatuses.Cancelled),
    };

    // ── Conflicts (from the live conflict service) ────────────────────────────

    private sealed record ConflictPoint(DateTime StartTs, string Kind);

    /// <summary>
    /// Flattens the live conflict registry into (scheduled-start, kind) points so conflicts can be
    /// bucketed by when they occur. Joins each conflict's request back to its start_ts/site via the
    /// scheduled-request set (the conflict registry itself carries no timestamp). Site-filtered here —
    /// site-neutral requests (no site) are kept under every site.
    /// </summary>
    private async Task<List<ConflictPoint>> LoadConflictTimelineAsync(
        DateTime from, DateTime to, Guid? siteId, CancellationToken ct)
    {
        var registry = await conflictService.GetAllAsync(from, to, ct);
        if (registry.Count == 0) return [];

        var scheduled = (await requestRepository.GetScheduledAsync(from, to, ct))
            .ToDictionary(r => r.Id);

        var points = new List<ConflictPoint>();
        foreach (var rc in registry)
        {
            if (!scheduled.TryGetValue(rc.RequestId, out var request)) continue;
            // Exclude only when the request is bound to a *different* site; site-neutral stays in.
            if (siteId.HasValue && request.SiteId.HasValue && request.SiteId != siteId) continue;
            if (request.StartTs is not { } start) continue;
            foreach (var c in rc.Conflicts)
                points.Add(new ConflictPoint(start, c.Kind));
        }
        return points;
    }

    /// <summary>Maps live <c>ConflictInfo.Kind</c> values into the stable analytics categories.</summary>
    private static ConflictCounts CountConflicts(IEnumerable<string> kinds)
    {
        int total = 0, overbooking = 0, criteria = 0, unavailable = 0, outside = 0;
        foreach (var kind in kinds)
        {
            total++;
            switch (kind)
            {
                case "overlap":
                case "capacity_exceeded":
                    overbooking++; break;
                case "connector_mismatch":
                    criteria++; break;
                case "starts_in_off_time":
                case "site_mismatch":
                    unavailable++; break;
                case "below_min_duration":
                case "before_earliest_start":
                case "after_latest_end":
                    outside++; break;
            }
        }
        return new ConflictCounts
        {
            Total = total,
            Overbooking = overbooking,
            CriteriaMismatch = criteria,
            ResourceUnavailable = unavailable,
            ScheduleOutsideAvailability = outside,
            MissingResource = 0, // no live kind maps here yet — honest 0, not faked
        };
    }

    // ── Utilization (time-based occupancy) ────────────────────────────────────

    /// <summary>One bucket's raw capacity/used minutes (pre-rounding) — the single computation the
    /// trend chart and the overview KPI both consume, so the headline can't disagree with the chart.</summary>
    private sealed record UtilBucket(DateTime Start, DateTime End, double CapMinutes, double UsedMinutes);

    /// <summary>
    /// Per-bucket capacity/used minutes for one resource type, computed as *time-based occupancy* — the
    /// share of available time actually booked over the bucket. This deliberately differs from the
    /// scheduler grid's per-slot view (<see cref="IUtilizationService"/>), where an Exclusive resource
    /// reads 100% if occupied at all in a slot: at month/quarter granularity that pins utilization at
    /// 100% for any month with a single booking. Here:
    ///   capacity_r = base availability × the bucket's open (non-blocked) minutes
    ///   used_r     = Σ (allocation% × overlap minutes), capped at capacity_r so overbooking surfaces
    ///                as a conflict, not as &gt;100% utilization
    /// Resource selection (incl. site resolution) and blocked periods reuse the same repositories the
    /// grid uses, so only the metric — not the data sourcing — is bespoke.
    /// </summary>
    private async Task<List<UtilBucket>> ComputeUtilizationSeriesAsync(
        string resourceType, DateTime from, DateTime to, string bucket, Guid? siteId, CancellationToken ct)
    {
        var buckets = InsightsBuckets.Generate(from, to, bucket);
        if (buckets.Count == 0) return [];
        var rangeFrom = buckets[0].Start;
        var rangeTo = buckets[^1].End;

        var resources = await resourceRepository.GetAllAsync(new ResourceListFilter
        {
            IsActive = true,
            ResourceTypeKey = resourceType,
            SiteId = siteId,
            SiteWindowFrom = siteId.HasValue ? rangeFrom : null,
            SiteWindowTo = siteId.HasValue ? rangeTo : null,
        }, ct);

        var cap = new double[buckets.Count];
        var used = new double[buckets.Count];

        // Bulk-preload assignments + blocked periods for all resources up front (was N+1: two DB
        // round-trips per resource). The per-bucket math below is unchanged.
        var resourceIds = resources.Select(r => r.Id).ToList();
        var assignmentsByResource = (await assignmentRepository.GetActiveByResourcesAsync(resourceIds, rangeFrom, rangeTo, ct))
            .GroupBy(a => a.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var blockedByResource = await availabilityResolver.GetBlockedPeriodsForResourcesAsync(resourceIds, ct);

        foreach (var resource in resources)
        {
            var assignments = assignmentsByResource.GetValueOrDefault(resource.Id, []);
            var blocked = blockedByResource.GetValueOrDefault(resource.Id, []);

            for (var i = 0; i < buckets.Count; i++)
            {
                var (bs, be) = buckets[i];
                var span = (be - bs).TotalMinutes;

                var blockedMin = blocked.Sum(p => OverlapMinutes(p.StartTs, p.EndTs, bs, be));
                var openMin = Math.Max(0, span - blockedMin);
                var capacityR = resource.BaseAvailabilityPercent / 100.0 * openMin;

                var occupied = 0.0;
                foreach (var a in assignments)
                {
                    if (a.AssignmentStatus == AssignmentStatuses.Cancelled) continue;
                    var overlap = OverlapMinutes(a.StartUtc, a.EndUtc, bs, be);
                    if (overlap <= 0) continue;
                    occupied += (double)(a.AllocationPercent ?? 100m) / 100.0 * overlap;
                }

                cap[i] += capacityR;
                used[i] += Math.Min(occupied, capacityR);
            }
        }

        var result = new List<UtilBucket>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
            result.Add(new UtilBucket(buckets[i].Start, buckets[i].End, cap[i], used[i]));
        return result;
    }

    private static double OverlapMinutes(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? (end - start).TotalMinutes : 0;
    }

    /// <summary>
    /// Aggregate utilization over a series: Σ used / Σ capacity. Bucket-granularity-invariant, so the
    /// overview KPI equals the total the trend chart sums to (and therefore ≤ the chart's peak bucket).
    /// Null when no capacity is configured (e.g. tools without an availability model) — never a fake 0%.
    /// </summary>
    private static decimal? AggregatePercent(IReadOnlyCollection<UtilBucket> series)
    {
        var cap = series.Sum(s => s.CapMinutes);
        var used = series.Sum(s => s.UsedMinutes);
        return cap > 0 ? Math.Round((decimal)(used / cap * 100.0), 2) : null;
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    private static (DateTime From, DateTime To) Bounds(
        IReadOnlyList<(DateTime Start, DateTime End)> buckets, InsightsFilter filter)
        => buckets.Count > 0 ? (buckets[0].Start, buckets[^1].End) : (filter.From, filter.To);

    private static InsightsMetadata Metadata() => new()
    {
        CalculatedAt = DateTime.UtcNow,
        SourceMode = SourceMode,
    };
}
