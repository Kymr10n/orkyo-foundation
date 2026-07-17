using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Security.Features;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace Api.Endpoints;

/// <summary>
/// Tenant-scoped audit log for tenant admins (<c>GET /api/audit</c>). Reads the current
/// tenant's OWN database — <c>audit_events</c> there IS the tenant audit trail, so isolation
/// is physical (no <c>tenant_id</c> filter) rather than a shared-table predicate. This is the
/// canonical store for both SaaS tenants and single-tenant Community. Unlike the site-admin
/// <c>/api/admin/audit</c> (control-plane, all tenants, sensitive fields), this omits
/// <c>ip_address</c>/<c>request_id</c> and is gated behind the <c>audit_log</c> feature
/// (Professional+ in SaaS; always on in Community via the allow-all feature gate).
///
/// Platform actors (site-admin break-glass, BFF sign-in) aren't tenant-DB users, so the
/// <c>users</c> join won't resolve them; those events carry <c>metadata.source = "platform"</c>
/// and a denormalized actor email for the UI.
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

        // The connection targets THIS tenant's database, so every row already belongs to it —
        // no tenant_id predicate (and the tenant DB's audit_events has no such column).
        var org = currentTenant.ToOrgContext();

        var clauses = new List<string>();
        var parameters = new List<NpgsqlParameter>();
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
        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : string.Empty;

        await using var conn = connectionFactory.CreateOrgConnection(org);

        var result = await conn.QueryPagedAsync(
            new PageRequest { Page = page, PageSize = pageSize },
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
            },
            ct);

        // Preserve the existing wire shape (events/totalCount) over the PagedResult envelope.
        return Results.Ok(new
        {
            events = result.Items,
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalItems,
            totalPages = result.TotalPages,
        });
    }
}
