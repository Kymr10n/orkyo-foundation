using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IPlatformAuditEventRepository
{
    Task<PagedResult<AuditEventDto>> ListAsync(AuditEventListFilter filter, PageRequest page, CancellationToken ct = default);
}

public interface ITenantAuditEventRepository
{
    Task<PagedResult<TenantAuditEventDto>> ListAsync(AuditEventListFilter filter, PageRequest page, CancellationToken ct = default);
}

/// <summary>
/// The platform audit: <c>audit_events</c> in the control plane, read by site admins.
/// </summary>
public sealed class PlatformAuditEventRepository(IDbConnectionFactory connectionFactory) : IPlatformAuditEventRepository
{
    private const string SelectColumns =
        "id, actor_user_id, actor_type, action, target_type, target_id, " +
        "metadata::text AS metadata, request_id, ip_address, created_at";

    public async Task<PagedResult<AuditEventDto>> ListAsync(AuditEventListFilter filter, PageRequest page, CancellationToken ct = default)
    {
        var (where, parameters) = AuditEventSql.BuildWhere("", filter);
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        return await conn.QueryPagedAsync(
            page,
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
    }
}

/// <summary>
/// The tenant audit: <c>audit_events</c> in the caller's tenant database. The connection
/// targets that database, so every row already belongs to the tenant — no tenant_id
/// predicate (and the tenant DB's audit_events has no such column).
/// </summary>
public sealed class TenantAuditEventRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : ITenantAuditEventRepository
{
    private const string SelectColumns =
        "a.id, a.actor_user_id, u.email, u.display_name, a.actor_type, a.action, " +
        "a.target_type, a.target_id, a.metadata::text AS metadata, a.created_at";

    public async Task<PagedResult<TenantAuditEventDto>> ListAsync(AuditEventListFilter filter, PageRequest page, CancellationToken ct = default)
    {
        var (where, parameters) = AuditEventSql.BuildWhere("a.", filter);
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.QueryPagedAsync(
            page,
            $"SELECT COUNT(*) FROM audit_events a {where}",
            $@"SELECT {SelectColumns}
               FROM audit_events a
               LEFT JOIN users u ON u.id = a.actor_user_id
               {where}
               ORDER BY a.created_at DESC
               LIMIT @limit OFFSET @offset",
            bind: p => { foreach (var wp in parameters) p.Add(wp.Clone()); },
            map: reader => new TenantAuditEventDto
            {
                Id = reader.GetGuid("id"),
                ActorUserId = reader.GetNullableGuid("actor_user_id"),
                ActorEmail = reader.GetNullableString("email"),
                ActorDisplayName = reader.GetNullableString("display_name"),
                ActorType = reader.GetString("actor_type"),
                Action = reader.GetString("action"),
                TargetType = reader.GetNullableString("target_type"),
                TargetId = reader.GetNullableString("target_id"),
                Metadata = reader.GetNullableString("metadata"),
                CreatedAt = reader.GetDateTime("created_at"),
            },
            ct);
    }
}

/// <summary>The WHERE clause both audit listings share.</summary>
internal static class AuditEventSql
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
}
