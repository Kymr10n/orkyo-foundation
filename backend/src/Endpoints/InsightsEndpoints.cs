using Api.Helpers;
using Api.Middleware;
using Api.Models.Insights;
using Api.Repositories;
using Api.Services;
using Api.Services.Insights;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

/// <summary>
/// Built-in Insights dashboard — in-app, session-authenticated, tenant-scoped analytics for the
/// Utilization → Insights tab. Aggregated and chart-ready (distinct from the token-authenticated,
/// row-level Reporting API). Available to all tiers. Tenant is implicit (per-database isolation);
/// only <c>siteId</c> is an explicit dimension and is validated against the tenant.
/// </summary>
public static class InsightsEndpoints
{
    // Overview has no bucket; cap its scan to keep it bounded (UI default is last 12 months).
    private const int OverviewMaxRangeDays = 5 * 366;

    /// <summary>
    /// Bottlenecks measure per day, so they get a tighter cap than the overview's five years —
    /// the finest granularity on the widest window is the scan InsightsBuckets.MaxRangeDays
    /// exists to prevent.
    ///
    /// Two years, matching the week bucket (the finest the trends offer). It has to clear the
    /// dashboard's own default filter, "Last 6 / next 12 months", which is roughly 550 days: a
    /// cap that rejects the range the page opens on is not a guard, it is a broken tab.
    /// </summary>
    private static int BottlenecksMaxRangeDays => InsightsBuckets.MaxRangeDays("week");

    public static void MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights")
            .WithTags("Insights")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        group.MapGet("/overview", GetOverview)
            .WithName("GetInsightsOverview")
            .WithSummary("KPI cards for the selected period and optional site");

        group.MapGet("/utilization", GetUtilization)
            .WithName("GetInsightsUtilization")
            .WithSummary("Capacity vs. used utilization trend by bucket for a resource type");

        group.MapGet("/conflicts", GetConflicts)
            .WithName("GetInsightsConflicts")
            .WithSummary("Conflict trend by type and bucket");

        group.MapGet("/requests", GetRequests)
            .WithName("GetInsightsRequests")
            .WithSummary("Request status trend and totals by bucket");

        group.MapGet("/bottlenecks", GetBottlenecks)
            .WithName("GetInsightsBottlenecks")
            .WithSummary("Resources booked beyond their capacity, worst first");
    }

    private static async Task<IResult> GetBottlenecks(
        DateTime? from, DateTime? to, Guid? siteId, string? resourceType,
        IInsightsService svc, ISiteRepository sites, IResourceTypeService resourceTypes,
        CancellationToken ct)
    {
        // An all-whitespace value is not a filter. Left as-is it slips past the validator below
        // and then matches no resource type at all, answering 200 with an empty ranking where the
        // sibling endpoints answer 400.
        resourceType = string.IsNullOrWhiteSpace(resourceType) ? null : resourceType;

        if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;

        // resourceType narrows the ranking here; it does not choose a series as it does for the
        // utilization trend, so omitting it means "every type" rather than an incomplete request.
        // ValidateResourceTypeAsync rejects a blank one, so it only runs when there is one to check.
        if (resourceType is not null
            && await ValidateResourceTypeAsync(resourceType, resourceTypes, ct) is { } rErr) return rErr;

        if ((t - f).TotalDays > BottlenecksMaxRangeDays)
            return ErrorResponses.BadRequest("Date range too large.");
        if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

        var filter = new InsightsFilter { SiteId = siteId, From = f, To = t, ResourceType = resourceType };
        return Results.Ok(await svc.GetBottlenecksAsync(filter, ct));
    }

    private static async Task<IResult> GetOverview(
        DateTime? from, DateTime? to, Guid? siteId,
        IInsightsService svc, ISiteRepository sites,
        CancellationToken ct)
    {
        if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
        if ((t - f).TotalDays > OverviewMaxRangeDays)
            return ErrorResponses.BadRequest("Date range too large.");
        if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

        var filter = new InsightsFilter { SiteId = siteId, From = f, To = t };
        return Results.Ok(await svc.GetOverviewAsync(filter, ct));
    }

    private static async Task<IResult> GetUtilization(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket, string? resourceType,
        IInsightsService svc, ISiteRepository sites, IResourceTypeService resourceTypes,
        CancellationToken ct)
    {
        if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
        if (ValidateBucket(bucket) is { } bErr) return bErr;
        if (await ValidateResourceTypeAsync(resourceType, resourceTypes, ct) is { } rErr) return rErr;
        if (ValidateRange(f, t, bucket!) is { } rangeErr) return rangeErr;
        if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

        var filter = new InsightsFilter
        {
            SiteId = siteId,
            From = f,
            To = t,
            Bucket = bucket,
            ResourceType = resourceType,
        };
        return Results.Ok(await svc.GetUtilizationTrendAsync(filter, ct));
    }

    private static async Task<IResult> GetConflicts(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket,
        IInsightsService svc, ISiteRepository sites,
        CancellationToken ct)
    {
        if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
        if (ValidateBucket(bucket) is { } bErr) return bErr;
        if (ValidateRange(f, t, bucket!) is { } rangeErr) return rangeErr;
        if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

        var filter = new InsightsFilter { SiteId = siteId, From = f, To = t, Bucket = bucket };
        return Results.Ok(await svc.GetConflictTrendAsync(filter, ct));
    }

    private static async Task<IResult> GetRequests(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket,
        IInsightsService svc, ISiteRepository sites,
        CancellationToken ct)
    {
        if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
        if (ValidateBucket(bucket) is { } bErr) return bErr;
        if (ValidateRange(f, t, bucket!) is { } rangeErr) return rangeErr;
        if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

        var filter = new InsightsFilter { SiteId = siteId, From = f, To = t, Bucket = bucket };
        return Results.Ok(await svc.GetRequestTrendAsync(filter, ct));
    }

    // ── Validation (fail fast, no silent defaults) ────────────────────────────

    private static IResult? ValidatePeriod(DateTime? from, DateTime? to, out DateTime f, out DateTime t)
    {
        f = default; t = default;
        if (from is null || to is null)
            return ErrorResponses.BadRequest("'from' and 'to' are required.");
        if (from >= to)
            return ErrorResponses.BadRequest("'from' must be before 'to'.");
        f = from.Value; t = to.Value;
        return null;
    }

    private static IResult? ValidateBucket(string? bucket)
    {
        if (string.IsNullOrWhiteSpace(bucket))
            return ErrorResponses.BadRequest("'bucket' is required (week|month|quarter|year).");
        if (!InsightsBuckets.ValidBuckets.Contains(bucket))
            return ErrorResponses.BadRequest($"Invalid bucket '{bucket}'. Expected week|month|quarter|year.");
        return null;
    }

    /// <summary>
    /// Validates against the resource types this tenant actually has, not a fixed space|person|tool
    /// list — a workspace that defines "Vehicle" must be able to chart it. Inactive types are
    /// rejected: they are out of planning, so their series would be a flat line with no meaning.
    /// The valid keys are echoed back because they differ per tenant and are not guessable.
    /// </summary>
    private static async Task<IResult?> ValidateResourceTypeAsync(
        string? resourceType, IResourceTypeService resourceTypes, CancellationToken ct)
    {
        var active = await resourceTypes.GetAllAsync(isActive: true, ct: ct);
        var keys = string.Join('|', active.Select(t => t.Key));

        if (string.IsNullOrWhiteSpace(resourceType))
            return ErrorResponses.BadRequest($"'resourceType' is required ({keys}).");
        if (!active.Any(t => string.Equals(t.Key, resourceType, StringComparison.Ordinal)))
            return ErrorResponses.BadRequest($"Invalid resourceType '{resourceType}'. Expected {keys}.");
        return null;
    }

    private static IResult? ValidateRange(DateTime from, DateTime to, string bucket)
        => (to - from).TotalDays > InsightsBuckets.MaxRangeDays(bucket)
            ? ErrorResponses.BadRequest($"Date range too large for bucket '{bucket}'.")
            : null;

    private static async Task<IResult?> ValidateSiteAsync(Guid? siteId, ISiteRepository sites, CancellationToken ct)
    {
        if (siteId is null) return null;
        var exists = await sites.ExistsAsync(siteId.Value, ct);
        return exists ? null : ErrorResponses.NotFound("Site", siteId);
    }
}
