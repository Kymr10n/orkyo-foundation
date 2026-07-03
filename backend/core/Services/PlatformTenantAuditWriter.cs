namespace Api.Services;

/// <summary>
/// Writes an audit event into a TARGET tenant's own database from a platform context
/// (site-admin break-glass, BFF sign-in, SaaS staff actions) where the actor is NOT a
/// member of that tenant's database.
///
/// The tenant <c>audit_events.actor_user_id</c> FKs to the tenant's <c>users</c> table, and
/// platform staff have no row there, so the actor id is written as NULL (actor_type
/// <c>system</c>) and the actor's identity is denormalized into metadata alongside
/// <c>source = "platform"</c>. The tenant audit-log UI uses that to label the row and show who
/// on the platform acted. This is how platform actions on a tenant surface in the tenant's
/// own audit trail without leaking platform identities into tenant data.
/// </summary>
public interface IPlatformTenantAuditWriter
{
    /// <summary>
    /// Best-effort: resolves the target tenant by slug and records the event in its database.
    /// An unresolvable slug or DB error is logged, never thrown — auditing must not fail the
    /// operation it documents.
    /// </summary>
    Task WriteAsync(
        string tenantSlug,
        string action,
        Guid actorUserId,
        string? actorEmail,
        string? targetType = null,
        string? targetId = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken ct = default);
}

public sealed class PlatformTenantAuditWriter : IPlatformTenantAuditWriter
{
    private readonly ITenantResolver _tenantResolver;
    private readonly ITenantUserService _tenantUserService;
    private readonly ILogger<PlatformTenantAuditWriter> _logger;

    public PlatformTenantAuditWriter(
        ITenantResolver tenantResolver,
        ITenantUserService tenantUserService,
        ILogger<PlatformTenantAuditWriter> logger)
    {
        _tenantResolver = tenantResolver;
        _tenantUserService = tenantUserService;
        _logger = logger;
    }

    public async Task WriteAsync(
        string tenantSlug,
        string action,
        Guid actorUserId,
        string? actorEmail,
        string? targetType = null,
        string? targetId = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        try
        {
            var tenant = await _tenantResolver.ResolveTenantAsync(subdomain: null, tenantHeader: tenantSlug, ct);
            if (tenant is null)
            {
                _logger.LogWarning(
                    "Platform audit event {Action} not recorded — tenant slug {Slug} did not resolve", action, tenantSlug);
                return;
            }

            // Denormalize the platform actor into metadata — actor_user_id stays NULL (system) because
            // platform staff are not rows in the tenant's users table (FK would reject them).
            var enriched = new Dictionary<string, object?>
            {
                ["source"] = "platform",
                ["actorUserId"] = actorUserId.ToString(),
                ["actorEmail"] = actorEmail,
            };
            if (metadata is not null)
                foreach (var kv in metadata) enriched[kv.Key] = kv.Value;

            await _tenantUserService.RecordAuditEventAsync(
                tenant.ToOrgContext(), action, actorUserId: null, targetType, targetId, enriched, ct);
        }
        catch (Exception ex)
        {
            // Best-effort: a resolver/DB hiccup must never break the platform operation being audited
            // (break-glass grant, staff tier/membership/quota change).
            _logger.LogError(ex, "Failed to record platform audit event {Action} for tenant {Slug}", action, tenantSlug);
        }
    }
}
