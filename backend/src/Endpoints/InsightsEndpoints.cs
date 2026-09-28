using Api.Helpers;
using Api.Middleware;
using Api.Models.Insights;
using Api.Repositories;
using Api.Services;
using Api.Services.Insights;
using FluentValidation;
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
        IValidator<InsightsQuery> validator, CancellationToken ct)
    {
        // An all-whitespace value is not a filter. Left as-is it would match no resource type at
        // all, answering 200 with an empty ranking where the sibling endpoints answer 400.
        resourceType = string.IsNullOrWhiteSpace(resourceType) ? null : resourceType;

        var query = new InsightsQuery(InsightsView.Bottlenecks, from, to, siteId, ResourceType: resourceType);
        return await EndpointHelpers.ExecuteAsync(query, validator, async () =>
        {
            // resourceType narrows the ranking here; it does not choose a series as it does for the
            // utilization trend, so omitting it means "every type" rather than an incomplete request.
            if (resourceType is not null
                && await ValidateResourceTypeAsync(resourceType, resourceTypes, ct) is { } rErr) return rErr;
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;

            return Results.Ok(await svc.GetBottlenecksAsync(query.ToFilter(), ct));
        });
    }

    private static async Task<IResult> GetOverview(
        DateTime? from, DateTime? to, Guid? siteId,
        IInsightsService svc, ISiteRepository sites,
        IValidator<InsightsQuery> validator, CancellationToken ct)
    {
        var query = new InsightsQuery(InsightsView.Overview, from, to, siteId);
        return await EndpointHelpers.ExecuteAsync(query, validator, async () =>
        {
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;
            return Results.Ok(await svc.GetOverviewAsync(query.ToFilter(), ct));
        });
    }

    private static async Task<IResult> GetUtilization(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket, string? resourceType,
        IInsightsService svc, ISiteRepository sites, IResourceTypeService resourceTypes,
        IValidator<InsightsQuery> validator, CancellationToken ct)
    {
        var query = new InsightsQuery(InsightsView.Trend, from, to, siteId, bucket, resourceType);
        return await EndpointHelpers.ExecuteAsync(query, validator, async () =>
        {
            if (await ValidateResourceTypeAsync(resourceType, resourceTypes, ct) is { } rErr) return rErr;
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;
            return Results.Ok(await svc.GetUtilizationTrendAsync(query.ToFilter(), ct));
        });
    }

    private static async Task<IResult> GetConflicts(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket,
        IInsightsService svc, ISiteRepository sites,
        IValidator<InsightsQuery> validator, CancellationToken ct)
    {
        var query = new InsightsQuery(InsightsView.Trend, from, to, siteId, bucket);
        return await EndpointHelpers.ExecuteAsync(query, validator, async () =>
        {
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;
            return Results.Ok(await svc.GetConflictTrendAsync(query.ToFilter(), ct));
        });
    }

    private static async Task<IResult> GetRequests(
        DateTime? from, DateTime? to, Guid? siteId, string? bucket,
        IInsightsService svc, ISiteRepository sites,
        IValidator<InsightsQuery> validator, CancellationToken ct)
    {
        var query = new InsightsQuery(InsightsView.Trend, from, to, siteId, bucket);
        return await EndpointHelpers.ExecuteAsync(query, validator, async () =>
        {
            if (await ValidateSiteAsync(siteId, sites, ct) is { } siteErr) return siteErr;
            return Results.Ok(await svc.GetRequestTrendAsync(query.ToFilter(), ct));
        });
    }

    // ── Tenant lookups (the shape rules and range caps live in InsightsQueryValidator) ──────────

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

    private static async Task<IResult?> ValidateSiteAsync(Guid? siteId, ISiteRepository sites, CancellationToken ct)
    {
        if (siteId is null) return null;
        var exists = await sites.ExistsAsync(siteId.Value, ct);
        return exists ? null : ErrorResponses.NotFound("Site", siteId);
    }
}
