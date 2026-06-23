using Api.Helpers;
using Api.Middleware;
using Api.Models.Insights;
using Api.Repositories;
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

    public static void MapInsightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights")
            .WithTags("Insights")
            .RequireAuthorization()
            .RequireTenantMembership();

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
    }

    private static async Task<IResult> GetOverview(
        DateTime? from, DateTime? to, Guid? siteId,
        IInsightsService svc, ISiteRepository sites,
        ILogger<EndpointLoggerCategory> logger, CancellationToken ct)
        => await EndpointHelpers.ExecuteAsync(async () =>
        {
            if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
            if ((t - f).TotalDays > OverviewMaxRangeDays)
                return ErrorResponses.BadRequest("Date range too large.");
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

            var filter = new InsightsFilter { SiteId = siteId, From = f, To = t };
            return Results.Ok(await svc.GetOverviewAsync(filter, ct));
        }, logger, "insights overview");

    private static async Task<IResult> GetUtilization(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket, string? resourceType,
        IInsightsService svc, ISiteRepository sites,
        ILogger<EndpointLoggerCategory> logger, CancellationToken ct)
        => await EndpointHelpers.ExecuteAsync(async () =>
        {
            if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
            if (ValidateBucket(bucket) is { } bErr) return bErr;
            if (ValidateResourceType(resourceType) is { } rErr) return rErr;
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
        }, logger, "insights utilization");

    private static async Task<IResult> GetConflicts(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket,
        IInsightsService svc, ISiteRepository sites,
        ILogger<EndpointLoggerCategory> logger, CancellationToken ct)
        => await EndpointHelpers.ExecuteAsync(async () =>
        {
            if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
            if (ValidateBucket(bucket) is { } bErr) return bErr;
            if (ValidateRange(f, t, bucket!) is { } rangeErr) return rangeErr;
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

            var filter = new InsightsFilter { SiteId = siteId, From = f, To = t, Bucket = bucket };
            return Results.Ok(await svc.GetConflictTrendAsync(filter, ct));
        }, logger, "insights conflicts");

    private static async Task<IResult> GetRequests(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket,
        IInsightsService svc, ISiteRepository sites,
        ILogger<EndpointLoggerCategory> logger, CancellationToken ct)
        => await EndpointHelpers.ExecuteAsync(async () =>
        {
            if (ValidatePeriod(from, to, out var f, out var t) is { } err) return err;
            if (ValidateBucket(bucket) is { } bErr) return bErr;
            if (ValidateRange(f, t, bucket!) is { } rangeErr) return rangeErr;
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

            var filter = new InsightsFilter { SiteId = siteId, From = f, To = t, Bucket = bucket };
            return Results.Ok(await svc.GetRequestTrendAsync(filter, ct));
        }, logger, "insights requests");

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

    private static IResult? ValidateResourceType(string? resourceType)
    {
        if (string.IsNullOrWhiteSpace(resourceType))
            return ErrorResponses.BadRequest("'resourceType' is required (space|person|tool).");
        if (!InsightsBuckets.ValidResourceTypes.Contains(resourceType))
            return ErrorResponses.BadRequest($"Invalid resourceType '{resourceType}'. Expected space|person|tool.");
        return null;
    }

    private static IResult? ValidateRange(DateTime from, DateTime to, string bucket)
        => (to - from).TotalDays > InsightsBuckets.MaxRangeDays(bucket)
            ? ErrorResponses.BadRequest($"Date range too large for bucket '{bucket}'.")
            : null;

    private static async Task<IResult?> ValidateSiteAsync(Guid? siteId, ISiteRepository sites, CancellationToken ct)
    {
        if (siteId is null) return null;
        var site = await sites.GetByIdAsync(siteId.Value, ct);
        return site is null ? ErrorResponses.NotFound("Site", siteId) : null;
    }
}
