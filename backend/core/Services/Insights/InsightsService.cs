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

    /// <summary>
    /// The most overloaded resources in the period, worst first. See the implementation for why
    /// this is measured over days regardless of the caller's bucket.
    /// </summary>
    Task<InsightsBottlenecks> GetBottlenecksAsync(InsightsFilter filter, CancellationToken ct = default);
}

public class InsightsService(
    OrgContext orgContext,
    IOrgDbConnectionFactory connectionFactory,
    IConflictTimelineProvider conflictTimeline,
    IResourceRepository resourceRepository,
    IResourceTypeService resourceTypeService,
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
        var conflicts = await conflictTimeline.GetAsync(filter.From, filter.To, filter.SiteId, ct);

        return new InsightsOverview
        {
            Period = new InsightsPeriod { From = filter.From, To = filter.To },
            SiteId = filter.SiteId,
            Requests = CountRequests(inWindow, backlog, DateTime.UtcNow),
            Conflicts = CountConflicts(conflicts.Select(c => c.Kind)),
            Utilization = await SummarizeUtilizationAsync(filter, ct),
            Metadata = Metadata(),
        };
    }

    /// <summary>
    /// Ranks resources by how far past their capacity they were booked.
    ///
    /// Measured over DAY buckets whatever bucket the caller asked for, and that is the whole
    /// point: overbooking is a spike. A machine slammed on nine days of a month and idle for the
    /// rest reads as comfortable at month granularity, and the days that hurt disappear into the
    /// average. Days are also what the scheduler plans in, so a day is the unit a planner can
    /// actually act on.
    ///
    /// Only resources with overspill are returned — an empty list is the healthy answer, not a
    /// missing one.
    /// </summary>
    public async Task<InsightsBottlenecks> GetBottlenecksAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        // Never empty: every caller validates from < to first (InsightsEndpoints.ValidatePeriod),
        // and any such range covers at least one day. The indexing below relies on that.
        var buckets = DaySlices(filter.From, filter.To);
        var period = new InsightsPeriod { From = filter.From, To = filter.To };

        var types = await resourceTypeService.GetAllAsync(isActive: true, ct: ct);
        var typeNames = types.ToDictionary(t => t.Key, t => t.DisplayName, StringComparer.Ordinal);

        var data = await LoadUtilizationInputsAsync(
            filter.ResourceType, buckets[0].Start, buckets[^1].End, filter.SiteId, ct);

        var items = new List<BottleneckResource>();
        foreach (var resource in data.Resources)
        {
            var (capacity, occupied) = AccumulateOne(resource, buckets, data);

            var overbooked = 0.0;
            var totalCapacity = 0.0;
            double? peakPercent = null;
            for (var i = 0; i < buckets.Count; i++)
            {
                overbooked += Math.Max(0, occupied[i] - capacity[i]);
                totalCapacity += capacity[i];

                if (capacity[i] > 0)
                    peakPercent = Math.Max(peakPercent ?? 0, occupied[i] / capacity[i] * 100.0);
            }

            if (overbooked <= 0) continue;

            items.Add(new BottleneckResource
            {
                ResourceId = resource.Id,
                Name = resource.Name,
                ResourceTypeKey = resource.ResourceTypeKey,
                ResourceTypeDisplayName = typeNames.GetValueOrDefault(resource.ResourceTypeKey, resource.ResourceTypeKey),
                OverbookedMinutes = overbooked,
                CapacityMinutes = totalCapacity,
                PeakUtilizationPercent = peakPercent,
            });
        }

        return new InsightsBottlenecks
        {
            Period = period,
            SiteId = filter.SiteId,
            Items = items
                .OrderByDescending(i => i.OverbookedMinutes)
                .ThenBy(i => i.Name, StringComparer.Ordinal)
                .Take(BottleneckLimit)
                .ToList(),
            Metadata = Metadata(),
        };
    }

    /// <summary>
    /// How many resources the ranking returns. A shortlist is the product: a planner fixes the
    /// worst few, and a list of everything overbooked is a report nobody reads.
    /// </summary>
    private const int BottleneckLimit = 10;

    /// <summary>
    /// Calendar days covering [from, to), half-open like every other bucket here.
    ///
    /// Not added to <see cref="InsightsBuckets"/>: that type's ValidBuckets is the vocabulary
    /// the trend endpoints accept from callers, and "day" is an internal measurement choice of
    /// this ranking rather than a new option on the API.
    /// </summary>
    private static List<(DateTime Start, DateTime End)> DaySlices(DateTime from, DateTime to)
    {
        var cursor = DateTime.SpecifyKind(from.ToUniversalTime().Date, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(to.ToUniversalTime().Date, DateTimeKind.Utc);
        if (end < to.ToUniversalTime()) end = end.AddDays(1);

        var slices = new List<(DateTime, DateTime)>();
        while (cursor < end)
        {
            slices.Add((cursor, cursor.AddDays(1)));
            cursor = cursor.AddDays(1);
        }
        return slices;
    }

    /// <summary>
    /// One utilization figure per ACTIVE resource type, ordered as the type listing returns them.
    ///
    /// Driven by the resource_types table rather than a fixed space/person/tool triple: types are
    /// tenant data, so a workspace that defines "Vehicle" gets a figure for it with no code change.
    /// Inactive types are excluded — they no longer take part in planning, so a permanent "—" for
    /// them would be noise.
    ///
    /// The bucket is fixed at "month" because AggregatePercent sums capacity minutes before
    /// dividing, which is granularity-invariant; the choice only affects how the range is chunked.
    ///
    /// One read for every type, not one read per type. The resources of different types are
    /// disjoint sets, so fetching them all and grouping in memory gives the same answer as asking
    /// per type — and asks three questions instead of three per type, which on a workspace with
    /// nine of them was twenty-seven sequential round-trips on one scoped connection.
    /// </summary>
    private async Task<UtilizationSummary> SummarizeUtilizationAsync(
        InsightsFilter filter, CancellationToken ct)
    {
        var types = await resourceTypeService.GetAllAsync(isActive: true, ct: ct);
        var buckets = InsightsBuckets.Generate(filter.From, filter.To, "month");
        if (types.Count == 0 || buckets.Count == 0)
            return new UtilizationSummary { ByResourceType = [] };

        var data = await LoadUtilizationInputsAsync(
            resourceTypeKey: null, buckets[0].Start, buckets[^1].End, filter.SiteId, ct);

        var byResourceKey = data.Resources
            .GroupBy(r => r.ResourceTypeKey)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ResourceInfo>)g.ToList());

        var byType = types.Select(type =>
        {
            var resources = byResourceKey.GetValueOrDefault(type.Key, []);
            var series = Accumulate(resources, buckets, data);
            return new ResourceTypeUtilization
            {
                ResourceTypeKey = type.Key,
                DisplayName = type.DisplayName,
                DisplayNamePlural = type.DisplayNamePlural,
                Percent = AggregatePercent(series),
            };
        }).ToList();

        return new UtilizationSummary { ByResourceType = byType };
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
        var timeline = await conflictTimeline.GetAsync(rangeFrom, rangeTo, filter.SiteId, ct);

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
                SequenceViolation = counts.SequenceViolation,
            };
        }).ToList();

        return new InsightsConflicts { Bucket = bucket, Series = series, Metadata = Metadata() };
    }

    public async Task<InsightsUtilization> GetUtilizationTrendAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        var bucket = filter.Bucket!;
        var resourceType = filter.ResourceType!;
        var computed = await ComputeUtilizationSeriesAsync(resourceType, filter.From, filter.To, bucket, filter.SiteId, ct);
        var series = computed.Buckets;
        var rangeFrom = series.Count > 0 ? series[0].Start : filter.From;
        var rangeTo = series.Count > 0 ? series[^1].End : filter.To;
        var timeline = await conflictTimeline.GetAsync(rangeFrom, rangeTo, filter.SiteId, ct);

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
            ResourceCount = computed.ResourceCount,
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

    private static RequestCounts CountRequests(IReadOnlyCollection<RequestFact> inWindow, int backlog, DateTime now) => new()
    {
        Total = inWindow.Count + backlog,
        Scheduled = inWindow.Count(f => f.IsScheduled && f.Status != RequestStatuses.Cancelled),
        Unscheduled = backlog,
        // Completed counts EFFECTIVE (schedule-vs-now) Done, matching the request-trend series and the
        // read model — stored status is only ever new/cancelled/deferred; in_progress/done are derived
        // on read, so a stored-status count here would always report 0 completions in production.
        Completed = inWindow.Count(f => RequestStatusCalculator.Effective(
            EnumMapper.FromDbValue<RequestStatus>(f.Status), f.StartTs, f.EndTs, now) == RequestStatus.Done),
        Cancelled = inWindow.Count(f => f.Status == RequestStatuses.Cancelled),
    };

    // ── Conflicts (from the live conflict service) ────────────────────────────

    /// <summary>Maps live <c>ConflictInfo.Kind</c> values into the stable analytics categories.</summary>
    private static ConflictCounts CountConflicts(IEnumerable<string> kinds)
    {
        int total = 0, overbooking = 0, criteria = 0, unavailable = 0, outside = 0, sequence = 0;
        foreach (var kind in kinds)
        {
            total++;
            switch (kind)
            {
                case ConflictKinds.Overlap:
                case ConflictKinds.CapacityExceeded:
                    overbooking++; break;
                case ConflictKinds.ConnectorMismatch:
                    criteria++; break;
                case ConflictKinds.StartsInOffTime:
                case ConflictKinds.SiteMismatch:
                    unavailable++; break;
                case ConflictKinds.BelowMinDuration:
                case ConflictKinds.BeforeEarliestStart:
                case ConflictKinds.AfterLatestEnd:
                    outside++; break;
                case ConflictKinds.DependencyViolation:
                    sequence++; break;
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
            SequenceViolation = sequence,
        };
    }

    // ── Utilization (time-based occupancy) ────────────────────────────────────

    /// <summary>One bucket's raw capacity/used minutes (pre-rounding) — the single computation the
    /// trend chart and the overview KPI both consume, so the headline can't disagree with the chart.</summary>
    private sealed record UtilBucket(DateTime Start, DateTime End, double CapMinutes, double UsedMinutes);

    /// <summary>
    /// The series plus how many resources produced it. The count separates "this type has nothing at
    /// this site" from "it has resources whose capacity nets to zero" — two situations that look
    /// identical in the numbers and need different words on screen.
    /// </summary>
    private sealed record UtilSeries(List<UtilBucket> Buckets, int ResourceCount);

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
    /// <summary>The rows every utilization figure is computed from, read once for a window.</summary>
    private sealed record UtilizationInputs(
        IReadOnlyList<ResourceInfo> Resources,
        IReadOnlyDictionary<Guid, List<ResourceAssignmentInfo>> AssignmentsByResource,
        IReadOnlyDictionary<Guid, List<BlockedPeriod>> BlockedByResource);

    /// <summary>
    /// Loads the resources of one type — or of every type, when <paramref name="resourceTypeKey"/>
    /// is null — together with their assignments and blocked periods.
    /// </summary>
    /// <remarks>
    /// Three round-trips whatever the size of the answer. The assignments and blocked periods were
    /// an N+1 per resource once; keeping the bulk reads in one place is what stops that returning.
    /// </remarks>
    private async Task<UtilizationInputs> LoadUtilizationInputsAsync(
        string? resourceTypeKey, DateTime rangeFrom, DateTime rangeTo, Guid? siteId, CancellationToken ct)
    {
        // Every resource: this feeds a capacity series, where a cut pool reads as lower demand
        // rather than as missing data.
        var resources = await resourceRepository.GetEveryAsync(new ResourceListFilter
        {
            IsActive = true,
            ResourceTypeKey = resourceTypeKey,
            SiteId = siteId,
            SiteWindowFrom = siteId.HasValue ? rangeFrom : null,
            SiteWindowTo = siteId.HasValue ? rangeTo : null,
        }, ct);

        var resourceIds = resources.Select(r => r.Id).ToList();
        var assignmentsByResource = (await assignmentRepository.GetActiveByResourcesAsync(resourceIds, rangeFrom, rangeTo, ct))
            .GroupBy(a => a.ResourceId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var blockedByResource = await availabilityResolver.GetBlockedPeriodsForResourcesAsync(resourceIds, ct);

        return new UtilizationInputs(resources, assignmentsByResource, blockedByResource);
    }

    /// <summary>
    /// Sums capacity and occupied minutes per bucket over a set of resources. Pure — the reads
    /// happened in <see cref="LoadUtilizationInputsAsync"/>, so the trend chart and the overview
    /// KPI share this arithmetic instead of each carrying a copy of it.
    /// </summary>
    private static List<UtilBucket> Accumulate(
        IReadOnlyList<ResourceInfo> resources,
        IReadOnlyList<(DateTime Start, DateTime End)> buckets,
        UtilizationInputs data)
    {
        var cap = new double[buckets.Count];
        var used = new double[buckets.Count];

        foreach (var resource in resources)
        {
            var (capacityR, occupiedR) = AccumulateOne(resource, buckets, data);

            for (var i = 0; i < buckets.Count; i++)
            {
                cap[i] += capacityR[i];

                // Capped deliberately: utilization is "how much of the capacity was used", and
                // a resource booked beyond its capacity has still only got the capacity it has.
                // The overspill is a conflict, and the bottleneck ranking is where it is read.
                used[i] += Math.Min(occupiedR[i], capacityR[i]);
            }
        }

        var result = new List<UtilBucket>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
            result.Add(new UtilBucket(buckets[i].Start, buckets[i].End, cap[i], used[i]));
        return result;
    }

    /// <summary>
    /// One resource's capacity and occupied minutes per bucket, both uncapped.
    ///
    /// This is the arithmetic core: <see cref="Accumulate"/> sums and caps it for the trend,
    /// and the bottleneck ranking keeps the resource identity and reads the uncapped overspill.
    /// Two consumers, one definition of what occupancy means.
    /// </summary>
    private static (double[] Capacity, double[] Occupied) AccumulateOne(
        ResourceInfo resource,
        IReadOnlyList<(DateTime Start, DateTime End)> buckets,
        UtilizationInputs data)
    {
        var assignments = data.AssignmentsByResource.GetValueOrDefault(resource.Id, []);
        var blocked = data.BlockedByResource.GetValueOrDefault(resource.Id, []);

        var capacity = new double[buckets.Count];
        var occupied = new double[buckets.Count];

        for (var i = 0; i < buckets.Count; i++)
        {
            var (bs, be) = buckets[i];
            var span = (be - bs).TotalMinutes;

            var blockedMin = blocked.Sum(p => OverlapMinutes(p.StartTs, p.EndTs, bs, be));
            var openMin = Math.Max(0, span - blockedMin);
            capacity[i] = resource.BaseAvailabilityPercent / 100.0 * openMin;

            var total = 0.0;
            foreach (var a in assignments)
            {
                if (a.AssignmentStatus == AssignmentStatuses.Cancelled) continue;
                var overlap = OverlapMinutes(a.StartUtc, a.EndUtc, bs, be);
                if (overlap <= 0) continue;
                total += (double)(a.AllocationPercent ?? 100m) / 100.0 * overlap;
            }
            occupied[i] = total;
        }

        return (capacity, occupied);
    }

    private async Task<UtilSeries> ComputeUtilizationSeriesAsync(
        string resourceType, DateTime from, DateTime to, string bucket, Guid? siteId, CancellationToken ct)
    {
        var buckets = InsightsBuckets.Generate(from, to, bucket);
        if (buckets.Count == 0) return new UtilSeries([], 0);

        var data = await LoadUtilizationInputsAsync(
            resourceType, buckets[0].Start, buckets[^1].End, siteId, ct);

        return new UtilSeries(Accumulate(data.Resources, buckets, data), data.Resources.Count);
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
