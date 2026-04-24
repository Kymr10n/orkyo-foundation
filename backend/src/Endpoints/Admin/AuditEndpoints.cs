using Api.Middleware;
using Api.Models;
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
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .RequireRateLimiting("admin-operations")
            .WithTags("Admin")
            .WithMetadata(new SkipTenantResolutionAttribute());

        group.MapGet("/audit", GetAuditEvents)
            .RequireSiteAdmin()
            .WithName("AdminGetAuditEvents")
            .WithSummary("Query audit events with filtering and pagination");
    }

    private static async Task<IResult> GetAuditEvents(
        IDbConnectionFactory connectionFactory,
        ILogger<EndpointLoggerCategory> logger,
        string? action = null,
        string? actorId = null,
        string? targetType = null,
        string? targetId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        var filter = new AuditEventListFilter(
            Action: action,
            ActorUserId: Guid.TryParse(actorId, out var actorGuid) ? actorGuid : null,
            TargetType: targetType,
            TargetId: targetId,
            FromUtc: from?.ToUniversalTime(),
            ToUtc: to?.ToUniversalTime());

        var offset = (page - 1) * pageSize;

        // Get total count
        await using var countCmd = AuditEventCommandFactory.CreateCountCommand(conn, filter);
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        // Get page
        await using var cmd = AuditEventCommandFactory.CreateSelectPageCommand(conn, filter, pageSize, offset);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = await AuditEventReaderFlow.ReadEventsAsync(reader);

        var events = rows.Select(r => new AuditEventDto
        {
            Id = r.Id,
            ActorUserId = r.ActorUserId,
            ActorType = r.ActorType,
            Action = r.Action,
            TargetType = r.TargetType,
            TargetId = r.TargetId,
            Metadata = r.Metadata,
            RequestId = r.RequestId,
            IpAddress = r.IpAddress,
            CreatedAt = r.CreatedAt
        }).ToList();

        logger.LogInformation("Admin queried audit events: {Count} of {Total} (page {Page})",
            events.Count, totalCount, page);

        return Results.Ok(new
        {
            events,
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }
}
