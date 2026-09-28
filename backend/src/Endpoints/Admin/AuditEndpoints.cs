using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Api.Endpoints.Admin;

/// <summary>Filter for an audit_events list query. All members optional.</summary>
internal sealed record AuditEventListFilter(
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
        "metadata::text AS metadata, request_id, ip_address, created_at";

    public static void MapAuditEndpoints(this WebApplication app)
    {
        var group = app.MapSiteAdminGroup();

        group.MapGet("/audit", GetAuditEvents)
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
        await using var conn = connectionFactory.CreateControlPlaneConnection();

        var filter = new AuditEventListFilter(
            Action: action,
            ActorUserId: Guid.TryParse(actorId, out var actorGuid) ? actorGuid : null,
            TargetType: targetType,
            TargetId: targetId,
            FromUtc: from?.ToUniversalTime(),
            ToUtc: to?.ToUniversalTime());

        var (where, parameters) = AuditQuery.BuildWhere("", filter);

        var result = await conn.QueryPagedAsync(
            PageRequest.From(page, pageSize),
            $"SELECT COUNT(*) FROM audit_events {where}",
            $@"SELECT {SelectColumns}
               FROM audit_events
               {where}
               ORDER BY created_at DESC
               LIMIT @limit OFFSET @offset",
            bind: p => { foreach (var wp in parameters) p.Add(wp.Clone()); },
            map: reader => new AuditEventDto
            {
                Id = reader.GetGuid("id"),
                ActorUserId = reader.GetNullableGuid("actor_user_id"),
                ActorType = reader.GetString("actor_type"),
                Action = reader.GetString("action"),
                TargetType = reader.GetNullableString("target_type"),
                TargetId = reader.GetNullableString("target_id"),
                Metadata = reader.GetNullableString("metadata"),
                RequestId = reader.GetNullableString("request_id"),
                IpAddress = reader.GetNullableString("ip_address"),
                CreatedAt = reader.GetDateTime("created_at"),
            },
            ct);

        logger.LogInformation("Admin queried audit events: {Count} of {Total} (page {Page})",
            result.Items.Count, result.TotalItems, result.Page);

        return AuditQuery.Envelope(result);
    }
}

/// <summary>
/// The list query the platform audit (control plane) and the tenant audit (tenant database)
/// share: the same filters over <c>audit_events</c>, and the same response envelope.
/// </summary>
internal static class AuditQuery
{
    /// <summary>
    /// Builds the WHERE clause for <paramref name="filter"/>, or an empty string when nothing
    /// filters. <paramref name="alias"/> prefixes each column, e.g. <c>"a."</c> for a joined query.
    /// </summary>
    internal static (string Where, List<NpgsqlParameter> Parameters) BuildWhere(string alias, AuditEventListFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();

        if (!string.IsNullOrWhiteSpace(filter.Action))
        { clauses.Add($"{alias}action = @action"); parameters.Add(new NpgsqlParameter("action", filter.Action)); }
        if (filter.ActorUserId.HasValue)
        { clauses.Add($"{alias}actor_user_id = @actorId"); parameters.Add(new NpgsqlParameter("actorId", filter.ActorUserId.Value)); }
        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        { clauses.Add($"{alias}target_type = @targetType"); parameters.Add(new NpgsqlParameter("targetType", filter.TargetType)); }
        if (!string.IsNullOrWhiteSpace(filter.TargetId))
        { clauses.Add($"{alias}target_id = @targetId"); parameters.Add(new NpgsqlParameter("targetId", filter.TargetId)); }
        if (filter.FromUtc.HasValue)
        { clauses.Add($"{alias}created_at >= @from"); parameters.Add(new NpgsqlParameter("from", filter.FromUtc.Value)); }
        if (filter.ToUtc.HasValue)
        { clauses.Add($"{alias}created_at <= @to"); parameters.Add(new NpgsqlParameter("to", filter.ToUtc.Value)); }

        return (clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses), parameters);
    }

    /// <summary>The existing wire shape (events/totalCount), kept over the PagedResult envelope.</summary>
    internal static IResult Envelope<T>(PagedResult<T> result) => Results.Ok(new
    {
        events = result.Items,
        page = result.Page,
        pageSize = result.PageSize,
        totalCount = result.TotalItems,
        totalPages = result.TotalPages,
    });
}
