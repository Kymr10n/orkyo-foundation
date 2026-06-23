using Api.Constants;
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
/// Tenant scoping is implicit: every read goes through the per-request tenant/org connection.
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
    IUtilizationService utilizationService,
    IRequestRepository requestRepository) : IInsightsService
{
    // The in-app dashboard reports "live": request facts come from the live view and conflict/
    // utilization from live services. This is the swap point — a snapshot-backed view would carry
    // source_mode='snapshot', read here, without changing the API or UI.
    private const string SourceMode = "live";

    private static readonly string[] AllResourceTypes =
        [ResourceTypeKeys.Space, ResourceTypeKeys.Person, ResourceTypeKeys.Tool];

    public async Task<InsightsOverview> GetOverviewAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        var facts = await FetchRequestFactsAsync(filter.From, filter.To, filter.SiteId, ct);
        var conflicts = await LoadConflictTimelineAsync(filter.From, filter.To, filter.SiteId, ct);

        return new InsightsOverview
        {
            Period = new InsightsPeriod { From = filter.From, To = filter.To },
            SiteId = filter.SiteId,
            Requests = CountRequests(facts),
            Conflicts = CountConflicts(conflicts.Select(c => c.Kind)),
            Utilization = new UtilizationSummary
            {
                SpacesPercent = await ComputeUtilizationPercentAsync(ResourceTypeKeys.Space, filter, ct),
                PeoplePercent = await ComputeUtilizationPercentAsync(ResourceTypeKeys.Person, filter, ct),
                ToolsPercent = await ComputeUtilizationPercentAsync(ResourceTypeKeys.Tool, filter, ct),
            },
            Metadata = Metadata(),
        };
    }

    public async Task<InsightsRequests> GetRequestTrendAsync(InsightsFilter filter, CancellationToken ct = default)
    {
        var bucket = filter.Bucket!;
        var buckets = InsightsBuckets.Generate(filter.From, filter.To, bucket);
        var (rangeFrom, rangeTo) = Bounds(buckets, filter);
        var facts = await FetchRequestFactsAsync(rangeFrom, rangeTo, filter.SiteId, ct);

        var series = buckets.Select(b =>
        {
            var inBucket = facts.Where(f => f.CreatedAt >= b.Start && f.CreatedAt < b.End).ToList();
            var counts = CountRequests(inBucket);
            return new RequestSeriesPoint
            {
                BucketStart = b.Start,
                BucketEnd = b.End,
                Total = counts.Total,
                Scheduled = counts.Scheduled,
                Unscheduled = counts.Unscheduled,
                Completed = counts.Completed,
                Cancelled = counts.Cancelled,
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
        var buckets = InsightsBuckets.Generate(filter.From, filter.To, bucket);
        var rangeFrom = buckets.Count > 0 ? buckets[0].Start : filter.From;
        var rangeTo = buckets.Count > 0 ? buckets[^1].End : filter.To;

        // Per-resource buckets align by index with `buckets` (same aligned range + bucket grain).
        var byResource = await utilizationService.GetUtilizationByResourceAsync(
            resourceType, rangeFrom, rangeTo, bucket, filter.SiteId, ct);
        var timeline = await LoadConflictTimelineAsync(rangeFrom, rangeTo, filter.SiteId, ct);

        var series = buckets.Select((b, i) =>
        {
            var span = (b.End - b.Start).TotalMinutes;
            double totalCap = 0, used = 0;
            foreach (var r in byResource)
            {
                if (i >= r.Buckets.Count) continue;
                var rb = r.Buckets[i];
                totalCap += (double)rb.EffectiveAvailabilityPercent / 100.0 * span;
                used += (double)rb.AllocatedPercent / 100.0 * span;
            }

            var totalMin = (long)Math.Round(totalCap);
            var usedMin = (long)Math.Round(used);
            return new UtilizationSeriesPoint
            {
                BucketStart = b.Start,
                BucketEnd = b.End,
                TotalCapacityMinutes = totalMin,
                UsedCapacityMinutes = usedMin,
                AvailableCapacityMinutes = Math.Max(totalMin - usedMin, 0),
                UtilizationPercent = totalCap > 0 ? Math.Round((decimal)(used / totalCap * 100.0), 2) : null,
                ConflictCount = timeline.Count(c => c.StartTs >= b.Start && c.StartTs < b.End),
            };
        }).ToList();

        return new InsightsUtilization
        {
            ResourceType = resourceType,
            Bucket = bucket,
            Series = series,
            Metadata = Metadata(),
        };
    }

    // ── Request facts (from the analytics view) ───────────────────────────────

    private sealed record RequestFact(Guid Id, Guid? SiteId, string Status, bool IsScheduled, DateTime CreatedAt);

    private async Task<List<RequestFact>> FetchRequestFactsAsync(
        DateTime from, DateTime to, Guid? siteId, CancellationToken ct)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            SELECT request_id, site_id, status, is_scheduled, created_at
            FROM analytics_request_summary_v
            WHERE created_at >= @from AND created_at < @to
              AND (@siteId::uuid IS NULL OR site_id = @siteId)", db);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.AddWithValue("siteId", (object?)siteId ?? DBNull.Value);

        var facts = new List<RequestFact>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            facts.Add(new RequestFact(
                reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetDateTime(4)));
        return facts;
    }

    private static RequestCounts CountRequests(IReadOnlyCollection<RequestFact> facts) => new()
    {
        Total = facts.Count,
        Scheduled = facts.Count(f => f.IsScheduled && f.Status != "cancelled"),
        Unscheduled = facts.Count(f => !f.IsScheduled && f.Status != "cancelled"),
        Completed = facts.Count(f => f.Status == "done"),
        Cancelled = facts.Count(f => f.Status == "cancelled"),
    };

    // ── Conflicts (from the live conflict service) ────────────────────────────

    private sealed record ConflictPoint(DateTime StartTs, string Kind);

    /// <summary>
    /// Flattens the live conflict registry into (scheduled-start, kind) points so conflicts can be
    /// bucketed by when they occur. Joins each conflict's request back to its start_ts/site via the
    /// scheduled-request set (the conflict registry itself carries no timestamp). Site-filtered here.
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
            if (siteId.HasValue && request.SiteId != siteId) continue;
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

    // ── Utilization (from the live utilization service) ───────────────────────

    /// <summary>
    /// Aggregate utilization for one resource type over the period: Σ used-minutes / Σ capacity-minutes
    /// across all in-scope resources. Capacity-minutes derive from each resource's effective
    /// availability percent × bucket span; used-minutes from its allocated percent × span. Returns
    /// null when no capacity is configured (e.g. tools without an availability model) — never a fake 0%.
    /// </summary>
    private async Task<decimal?> ComputeUtilizationPercentAsync(
        string resourceType, InsightsFilter filter, CancellationToken ct)
    {
        var byResource = await utilizationService.GetUtilizationByResourceAsync(
            resourceType, filter.From, filter.To, "month", filter.SiteId, ct);

        double totalCap = 0, used = 0;
        foreach (var r in byResource)
            foreach (var b in r.Buckets)
            {
                var span = (b.End - b.Start).TotalMinutes;
                totalCap += (double)b.EffectiveAvailabilityPercent / 100.0 * span;
                used += (double)b.AllocatedPercent / 100.0 * span;
            }

        return totalCap > 0 ? Math.Round((decimal)(used / totalCap * 100.0), 2) : null;
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
