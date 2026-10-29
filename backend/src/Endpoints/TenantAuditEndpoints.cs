using Api.Endpoints.Admin;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Security.Features;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
        IFeatureGate featureGate,
        ITenantAuditEventRepository auditEvents,
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

        if (!AuditQuery.TryParseActorId(actorId, out var actorUserId))
            return ErrorResponses.BadRequest("actorId must be a GUID");

        // The repository reads THIS tenant's database (its OrgContext), so every row
        // already belongs to the tenant.
        var result = await auditEvents.ListAsync(new AuditEventListFilter(
            Action: action,
            ActorUserId: actorUserId,
            TargetType: targetType,
            TargetId: null,
            FromUtc: from?.ToUniversalTime(),
            ToUtc: to?.ToUniversalTime()), PageRequest.From(page, pageSize), ct);

        return AuditQuery.Envelope(result);
    }
}
