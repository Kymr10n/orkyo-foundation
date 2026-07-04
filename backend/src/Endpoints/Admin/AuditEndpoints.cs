using Api.Configuration;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Api.Endpoints.Admin;

/// <summary>Filter for the platform audit_events list query. All members optional.</summary>
public sealed record AuditEventListFilter(
    string? Action,
    Guid? ActorUserId,
    string? TargetType,
    string? TargetId,
    DateTime? FromUtc,
    DateTime? ToUtc);

public static class AuditEndpoints
{
    private const string SelectColumns =
        "id, actor_user_id, actor_type, action, target_type, target_id, " +
        "metadata::text, request_id, ip_address, created_at";

    public static void MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin")
            .RequireAuthorization()
            .RequireRateLimiting(FoundationRateLimitPolicies.AdminOperations)
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
        int pageSize = 50,
        CancellationToken ct = default)
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
        var (whereClause, whereParams) = BuildWhereClause(filter);

        await using var countCmd = new NpgsqlCommand(
            string.IsNullOrEmpty(whereClause)
                ? "SELECT COUNT(*) FROM audit_events"
                : $"SELECT COUNT(*) FROM audit_events {whereClause}", conn);
        foreach (var p in whereParams) countCmd.Parameters.Add(p.Clone());
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var prefix = string.IsNullOrEmpty(whereClause) ? string.Empty : whereClause + "\n            ";
        await using var pageCmd = new NpgsqlCommand($@"
            SELECT {SelectColumns}
            FROM audit_events
            {prefix}ORDER BY created_at DESC
            LIMIT @pageSize OFFSET @offset", conn);
        foreach (var p in whereParams) pageCmd.Parameters.Add(p.Clone());
        pageCmd.Parameters.AddWithValue("pageSize", pageSize);
        pageCmd.Parameters.AddWithValue("offset", offset);

        await using var reader = await pageCmd.ExecuteReaderAsync(ct);
        var events = new List<AuditEventDto>();
        while (await reader.ReadAsync(ct))
        {
            events.Add(new AuditEventDto
            {
                Id = reader.GetGuid(0),
                ActorUserId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ActorType = reader.GetString(2),
                Action = reader.GetString(3),
                TargetType = reader.IsDBNull(4) ? null : reader.GetString(4),
                TargetId = reader.IsDBNull(5) ? null : reader.GetString(5),
                Metadata = reader.IsDBNull(6) ? null : reader.GetString(6),
                RequestId = reader.IsDBNull(7) ? null : reader.GetString(7),
                IpAddress = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = reader.GetDateTime(9),
            });
        }

        logger.LogInformation("Admin queried audit events: {Count} of {Total} (page {Page})",
            events.Count, totalCount, page);

        return Results.Ok(new
        {
            events,
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        });
    }

    private static (string WhereClause, List<NpgsqlParameter> Parameters) BuildWhereClause(AuditEventListFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrWhiteSpace(filter.Action))
        { clauses.Add("action = @action"); parameters.Add(new NpgsqlParameter("action", filter.Action)); }
        if (filter.ActorUserId.HasValue)
        { clauses.Add("actor_user_id = @actorId"); parameters.Add(new NpgsqlParameter("actorId", filter.ActorUserId.Value)); }
        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        { clauses.Add("target_type = @targetType"); parameters.Add(new NpgsqlParameter("targetType", filter.TargetType)); }
        if (!string.IsNullOrWhiteSpace(filter.TargetId))
        { clauses.Add("target_id = @targetId"); parameters.Add(new NpgsqlParameter("targetId", filter.TargetId)); }
        if (filter.FromUtc.HasValue)
        { clauses.Add("created_at >= @from"); parameters.Add(new NpgsqlParameter("from", filter.FromUtc.Value)); }
        if (filter.ToUtc.HasValue)
        { clauses.Add("created_at <= @to"); parameters.Add(new NpgsqlParameter("to", filter.ToUtc.Value)); }

        return (clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses), parameters);
    }
}
