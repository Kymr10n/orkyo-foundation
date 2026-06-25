using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Models.Reporting;
using Api.Reporting;
using Api.Reporting.Auth;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Api.Services.Reporting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Endpoints.Reporting;

public static class ReportingEndpoints
{
    public static void MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/v1")
            .RequireAuthorization("ReportingToken")
            .RequireRateLimiting("reporting-api")
            .WithTags("Reporting API");

        // Verify token tenant matches resolved tenant (prevents cross-tenant token reuse)
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var record = ctx.HttpContext.Items[ReportingTokenContextKeys.TokenRecord]
                as ReportingTokenRecord;
            var currentTenant = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentTenant>();

            if (record is null || !currentTenant.HasTenant)
                return ErrorResponses.Unauthorized("invalid_reporting_token");

            if (record.TenantId != currentTenant.TenantId)
            {
                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ILogger<EndpointLoggerCategory>>();
                logger.LogWarning(
                    "Reporting token tenant mismatch: token={TokenTenant}, request={RequestTenant}",
                    record.TenantId, currentTenant.TenantId);
                return ErrorResponses.Forbidden();
            }

            // Reject explicit tenant parameters — tenant is always token-derived
            var query = ctx.HttpContext.Request.Query;
            if (query.ContainsKey("tenantId") || query.ContainsKey("tenantSlug"))
                return Results.Json(
                    new { error = "validation_failed", message = "tenantId and tenantSlug are not accepted." },
                    statusCode: StatusCodes.Status400BadRequest);

            return await next(ctx);
        });

        // Audit each reporting request (fire-and-forget, non-blocking)
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await next(ctx);
            sw.Stop();

            var record = ctx.HttpContext.Items[ReportingTokenContextKeys.TokenRecord]
                as ReportingTokenRecord;
            if (record is not null)
            {
                var audit = ctx.HttpContext.RequestServices.GetService<IAdminAuditService>();
                var statusCode = ctx.HttpContext.Response.StatusCode;
                _ = audit?.RecordEventAsync(
                    actorUserId: null,
                    action: "reporting.read",
                    targetType: ctx.HttpContext.Request.Path,
                    targetId: record.TenantId.ToString(),
                    metadata: new
                    {
                        actorType = "reporting_token",
                        tokenId = record.Id,
                        tokenPrefix = record.TokenPrefix,
                        statusCode,
                        durationMs = sw.ElapsedMilliseconds,
                    });
            }

            return result;
        });

        group.MapGet("/spaces/utilization", GetSpaceUtilization)
            .WithName("GetReportingSpaceUtilization")
            .WithSummary("Space utilization report for the authenticated tenant")
            .Produces<ReportingResult<SpaceUtilizationRow>>()
            .Produces(401).Produces(403);

        group.MapGet("/resources/utilization", GetResourceUtilization)
            .WithName("GetReportingResourceUtilization")
            .WithSummary("Resource utilization report for the authenticated tenant")
            .Produces<ReportingResult<ResourceUtilizationRow>>()
            .Produces(401).Produces(403);

        group.MapGet("/allocations", GetAllocations)
            .WithName("GetReportingAllocations")
            .WithSummary("All resource allocations in the given period")
            .Produces<ReportingResult<AllocationRow>>()
            .Produces(401).Produces(403);

        group.MapGet("/requests/throughput", GetRequestThroughput)
            .WithName("GetReportingRequestThroughput")
            .WithSummary("Request throughput metrics for the given period")
            .Produces<ReportingResult<RequestThroughputRow>>()
            .Produces(401).Produces(403);

        group.MapGet("/conflicts", GetConflicts)
            .WithName("GetReportingConflicts")
            .WithSummary("Resource overbooking conflicts in the given period")
            .Produces<ReportingResult<ConflictRow>>()
            .Produces(401).Produces(403);

        group.MapGet("/absences", GetAbsences)
            .WithName("GetReportingAbsences")
            .WithSummary("Resource absences in the given period")
            .Produces<ReportingResult<AbsenceRow>>()
            .Produces(401).Produces(403);

        group.MapGet("/capacity-vs-demand", GetCapacityVsDemand)
            .WithName("GetReportingCapacityVsDemand")
            .WithSummary("Capacity versus demand summary by resource type")
            .Produces<ReportingResult<CapacityVsDemandRow>>()
            .Produces(401).Produces(403);
    }

    private static async Task<IResult> GetSpaceUtilization(
        HttpContext ctx,
        IReportingQueryService qs,
        ICurrentTenant tenant,
        TenantContext tenantCtx,
        DateTime? from, DateTime? to, DateTime? updatedSince,
        int page = 1, int pageSize = ReportingPageRequest.DefaultPageSize,
        string? sort = null, string? format = null)
    {
        var q = BuildQuery(from, to, updatedSince, page, pageSize, sort, format);
        ValidateDateRange(q);
        ValidatePageSize(pageSize);

        var result = await qs.GetSpaceUtilizationAsync(
            tenant.TenantId, tenantCtx, q, ctx.RequestAborted);

        return ReportingCsvSerializer.IsCsvRequested(ctx.Request, format)
            ? ReportingCsvSerializer.ToCsvResult(result, "space-utilization.csv")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetResourceUtilization(
        HttpContext ctx,
        IReportingQueryService qs,
        ICurrentTenant tenant,
        TenantContext tenantCtx,
        DateTime? from, DateTime? to, DateTime? updatedSince,
        int page = 1, int pageSize = ReportingPageRequest.DefaultPageSize,
        string? sort = null, string? format = null)
    {
        var q = BuildQuery(from, to, updatedSince, page, pageSize, sort, format);
        ValidateDateRange(q);
        ValidatePageSize(pageSize);

        var result = await qs.GetResourceUtilizationAsync(
            tenant.TenantId, tenantCtx, q, ctx.RequestAborted);

        return ReportingCsvSerializer.IsCsvRequested(ctx.Request, format)
            ? ReportingCsvSerializer.ToCsvResult(result, "resource-utilization.csv")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetAllocations(
        HttpContext ctx,
        IReportingQueryService qs,
        ICurrentTenant tenant,
        TenantContext tenantCtx,
        DateTime? from, DateTime? to, DateTime? updatedSince,
        int page = 1, int pageSize = ReportingPageRequest.DefaultPageSize,
        string? sort = null, string? format = null)
    {
        var q = BuildQuery(from, to, updatedSince, page, pageSize, sort, format);
        ValidateDateRange(q);
        ValidatePageSize(pageSize);

        var result = await qs.GetAllocationsAsync(
            tenant.TenantId, tenantCtx, q, ctx.RequestAborted);

        return ReportingCsvSerializer.IsCsvRequested(ctx.Request, format)
            ? ReportingCsvSerializer.ToCsvResult(result, "allocations.csv")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetRequestThroughput(
        HttpContext ctx,
        IReportingQueryService qs,
        ICurrentTenant tenant,
        TenantContext tenantCtx,
        DateTime? from, DateTime? to,
        int page = 1, int pageSize = ReportingPageRequest.DefaultPageSize,
        string? format = null)
    {
        var q = BuildQuery(from, to, null, page, pageSize, null, format);
        ValidateDateRange(q);

        var result = await qs.GetRequestThroughputAsync(
            tenant.TenantId, tenantCtx, q, ctx.RequestAborted);

        return ReportingCsvSerializer.IsCsvRequested(ctx.Request, format)
            ? ReportingCsvSerializer.ToCsvResult(result, "request-throughput.csv")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetConflicts(
        HttpContext ctx,
        IReportingQueryService qs,
        ICurrentTenant tenant,
        TenantContext tenantCtx,
        DateTime? from, DateTime? to,
        int page = 1, int pageSize = ReportingPageRequest.DefaultPageSize,
        string? sort = null, string? format = null)
    {
        var q = BuildQuery(from, to, null, page, pageSize, sort, format);
        ValidateDateRange(q);
        ValidatePageSize(pageSize);

        var result = await qs.GetConflictsAsync(
            tenant.TenantId, tenantCtx, q, ctx.RequestAborted);

        return ReportingCsvSerializer.IsCsvRequested(ctx.Request, format)
            ? ReportingCsvSerializer.ToCsvResult(result, "conflicts.csv")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetAbsences(
        HttpContext ctx,
        IReportingQueryService qs,
        ICurrentTenant tenant,
        TenantContext tenantCtx,
        ITenantSettingsRepository settingsRepo,
        DateTime? from, DateTime? to,
        int page = 1, int pageSize = ReportingPageRequest.DefaultPageSize,
        string? sort = null, string? format = null)
    {
        var q = BuildQuery(from, to, null, page, pageSize, sort, format);
        ValidateDateRange(q);
        ValidatePageSize(pageSize);

        var allSettings = await settingsRepo.GetAllAsync(ctx.RequestAborted);
        var peopleLevelEnabled = allSettings.TryGetValue("reporting.people_level_enabled", out var v)
            && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

        var result = await qs.GetAbsencesAsync(
            tenant.TenantId, tenantCtx, q, peopleLevelEnabled, ctx.RequestAborted);

        return ReportingCsvSerializer.IsCsvRequested(ctx.Request, format)
            ? ReportingCsvSerializer.ToCsvResult(result, "absences.csv")
            : Results.Ok(result);
    }

    private static async Task<IResult> GetCapacityVsDemand(
        HttpContext ctx,
        IReportingQueryService qs,
        ICurrentTenant tenant,
        TenantContext tenantCtx,
        DateTime? from, DateTime? to,
        int page = 1, int pageSize = ReportingPageRequest.DefaultPageSize,
        string? granularity = "month", string? format = null)
    {
        var q = BuildQuery(from, to, null, page, pageSize, null, format) with { Granularity = granularity };
        ValidateDateRange(q);
        ValidatePageSize(pageSize);

        var result = await qs.GetCapacityVsDemandAsync(
            tenant.TenantId, tenantCtx, q, ctx.RequestAborted);

        return ReportingCsvSerializer.IsCsvRequested(ctx.Request, format)
            ? ReportingCsvSerializer.ToCsvResult(result, "capacity-vs-demand.csv")
            : Results.Ok(result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ReportingQuery BuildQuery(
        DateTime? from, DateTime? to, DateTime? updatedSince,
        int page, int pageSize, string? sort, string? format) =>
        new()
        {
            From = from,
            To = to,
            UpdatedSince = updatedSince,
            Page = page,
            PageSize = pageSize,
            Sort = sort,
            Format = format,
        };

    private static void ValidateDateRange(ReportingQuery q)
    {
        if (q.From.HasValue && q.To.HasValue && q.From > q.To)
            throw new ArgumentException("'from' must be before 'to'.");
    }

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize > ReportingPageRequest.MaxPageSize)
            throw new ArgumentException($"Maximum pageSize is {ReportingPageRequest.MaxPageSize}.");
    }
}
