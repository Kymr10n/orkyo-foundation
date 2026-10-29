using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Admin;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapSiteAdminGroup();

        group.MapGet("/audit", GetAuditEvents)
            .WithName("AdminGetAuditEvents")
            .WithSummary("Query audit events with filtering and pagination");
    }

    private static async Task<IResult> GetAuditEvents(
        IPlatformAuditEventRepository auditEvents,
        ILogger<EndpointLoggerCategory> logger,
        string? action = null,
        string? actorId = null,
        string? targetType = null,
        string? targetId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        if (!AuditQuery.TryParseActorId(actorId, out var actorUserId))
            return ErrorResponses.BadRequest("actorId must be a GUID");

        var filter = new AuditEventListFilter(
            Action: action,
            ActorUserId: actorUserId,
            TargetType: targetType,
            TargetId: targetId,
            FromUtc: from?.ToUniversalTime(),
            ToUtc: to?.ToUniversalTime());

        var result = await auditEvents.ListAsync(filter, PageRequest.From(page, pageSize), ct);

        logger.LogInformation("Admin queried audit events: {Count} of {Total} (page {Page})",
            result.Items.Count, result.TotalItems, result.Page);

        return AuditQuery.Envelope(result);
    }
}

/// <summary>
/// What the platform audit (control plane) and the tenant audit (tenant database) endpoints
/// share: the actor-id parsing and the response envelope. The query is in
/// <see cref="Api.Repositories.AuditEventSql"/>.
/// </summary>
internal static class AuditQuery
{
    /// <summary>
    /// An absent <paramref name="actorId"/> is no filter; a malformed one is refused rather than
    /// silently widening the query to every actor.
    /// </summary>
    internal static bool TryParseActorId(string? actorId, out Guid? actorUserId)
    {
        actorUserId = null;
        if (actorId is null) return true;
        if (!Guid.TryParse(actorId, out var parsed)) return false;
        actorUserId = parsed;
        return true;
    }

    internal static IResult Envelope<T>(PagedResult<T> result) => Results.Ok(new
    {
        events = result.Items,
        page = result.Page,
        pageSize = result.PageSize,
        totalCount = result.TotalItems,
        totalPages = result.TotalPages,
    });
}
