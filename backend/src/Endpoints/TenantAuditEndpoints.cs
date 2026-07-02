using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Security.Features;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Api.Endpoints;

/// <summary>
/// Tenant-scoped audit log for tenant admins (<c>GET /api/audit</c>). Unlike the site-admin
/// <c>/api/admin/audit</c> (all tenants, sensitive fields), this returns ONLY the current
/// tenant's events (<c>tenant_id = current tenant</c>) with <c>ip_address</c>/<c>request_id</c>
/// omitted, and is gated behind the <c>audit_log</c> feature (Professional+ in SaaS; always
/// on in Community via the allow-all feature gate).
/// </summary>
public static class TenantAuditEndpoints
{
    private const string SelectColumns =
        "a.id, a.actor_user_id, u.email, u.display_name, a.actor_type, a.action, " +
        "a.target_type, a.target_id, a.metadata::text, a.created_at";

    public static void MapTenantAuditEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/audit")
            .RequireAuthorization()
            .RequireAdminArea()
            .WithTags("Audit");

        group.MapGet("/", GetTenantAuditEvents)
            .WithName("GetTenantAuditEvents")
            .WithSummary("Query the current tenant's audit events (tenant admin, audit_log feature)");
    }

    private static async Task<IResult> GetTenantAuditEvents(
        ICurrentTenant currentTenant,
        IFeatureGate featureGate,
        IDbConnectionFactory connectionFactory,
        string? action = null,
        string? actorId = null,
        string? targetType = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        // Professional+ in SaaS; no-op (allow-all) in Community/foundation. Throws → 403.
        await featureGate.EnsureEnabledAsync(FeatureKeys.AuditLog, ct);

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 200) pageSize = 200;

        var tenantId = currentTenant.RequireTenantId();

        // Always tenant-scoped — tenant_id filter guarantees no cross-tenant leakage.
        var clauses = new List<string> { "a.tenant_id = @tenantId" };
        var parameters = new List<NpgsqlParameter> { new("tenantId", tenantId) };
        if (!string.IsNullOrWhiteSpace(action))
        { clauses.Add("a.action = @action"); parameters.Add(new("action", action)); }
        if (Guid.TryParse(actorId, out var actorGuid))
        { clauses.Add("a.actor_user_id = @actorId"); parameters.Add(new("actorId", actorGuid)); }
        if (!string.IsNullOrWhiteSpace(targetType))
        { clauses.Add("a.target_type = @targetType"); parameters.Add(new("targetType", targetType)); }
        if (from.HasValue)
        { clauses.Add("a.created_at >= @from"); parameters.Add(new("from", from.Value.ToUniversalTime())); }
        if (to.HasValue)
        { clauses.Add("a.created_at <= @to"); parameters.Add(new("to", to.Value.ToUniversalTime())); }
        var where = "WHERE " + string.Join(" AND ", clauses);

        await using var conn = connectionFactory.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var countCmd = new NpgsqlCommand($"SELECT COUNT(*) FROM audit_events a {where}", conn);
        foreach (var p in parameters) countCmd.Parameters.Add(p.Clone());
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var offset = (page - 1) * pageSize;
        await using var pageCmd = new NpgsqlCommand($@"
            SELECT {SelectColumns}
            FROM audit_events a
            LEFT JOIN users u ON u.id = a.actor_user_id
            {where}
            ORDER BY a.created_at DESC
            LIMIT @pageSize OFFSET @offset", conn);
        foreach (var p in parameters) pageCmd.Parameters.Add(p.Clone());
        pageCmd.Parameters.AddWithValue("pageSize", pageSize);
        pageCmd.Parameters.AddWithValue("offset", offset);

        await using var reader = await pageCmd.ExecuteReaderAsync(ct);
        var events = new List<TenantAuditEventDto>();
        while (await reader.ReadAsync(ct))
        {
            events.Add(new TenantAuditEventDto
            {
                Id = reader.GetGuid(0),
                ActorUserId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ActorEmail = reader.IsDBNull(2) ? null : reader.GetString(2),
                ActorDisplayName = reader.IsDBNull(3) ? null : reader.GetString(3),
                ActorType = reader.GetString(4),
                Action = reader.GetString(5),
                TargetType = reader.IsDBNull(6) ? null : reader.GetString(6),
                TargetId = reader.IsDBNull(7) ? null : reader.GetString(7),
                Metadata = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = reader.GetDateTime(9),
            });
        }

        return Results.Ok(new
        {
            events,
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
        });
    }
}
