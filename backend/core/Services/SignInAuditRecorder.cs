namespace Api.Services;

/// <summary>
/// Records a single <c>session.signed_in</c> event into the tenant's OWN database on sign-in, when
/// the user belongs to exactly one tenant (single-tenant SaaS / all Community). Multi-tenant users
/// authenticate once (user-level SSO) then choose a tenant, so there's no single tenant to attribute
/// the sign-in to — those are skipped (a per-tenant "first access" event would need per-request dedup;
/// deferred). The user is a tenant member, so the event is attributed to them (<c>actor_user_id</c>)
/// after ensuring their tenant-DB user stub exists (the audit FK needs it).
///
/// Called from <c>BffSessionEstablisher.EstablishAsync</c> — the single seam every login path goes
/// through (OIDC callback and SaaS demo login) — so every sign-in is audited once, regardless of how
/// the session was established.
/// </summary>
public interface ISignInAuditRecorder
{
    Task RecordAsync(Guid userId, string? email, CancellationToken ct = default);
}

public sealed class SignInAuditRecorder : ISignInAuditRecorder
{
    private readonly ISessionService _sessionService;
    private readonly ITenantResolver _tenantResolver;
    private readonly ITenantUserService _tenantUserService;
    private readonly ILogger<SignInAuditRecorder> _logger;

    public SignInAuditRecorder(
        ISessionService sessionService,
        ITenantResolver tenantResolver,
        ITenantUserService tenantUserService,
        ILogger<SignInAuditRecorder> logger)
    {
        _sessionService = sessionService;
        _tenantResolver = tenantResolver;
        _tenantUserService = tenantUserService;
        _logger = logger;
    }

    public async Task RecordAsync(Guid userId, string? email, CancellationToken ct = default)
    {
        try
        {
            var bootstrap = await _sessionService.BuildSessionResponseAsync(userId, ct);
            if (bootstrap is null || bootstrap.Tenants.Count != 1)
                return;

            var membership = bootstrap.Tenants[0];
            var tenant = await _tenantResolver.ResolveTenantAsync(subdomain: null, tenantHeader: membership.Slug, ct);
            if (tenant is null)
                return;

            var org = tenant.ToOrgContext();

            // Ensure the tenant-DB user stub exists so actor_user_id → users FK resolves; then attribute
            // the sign-in to the user. Without an email we can't create the stub, so fall back to a
            // system-actor event rather than risk an FK violation that would drop the row.
            Guid? actorUserId = null;
            if (!string.IsNullOrEmpty(email))
            {
                await _tenantUserService.CreateUserStubInTenantDatabaseAsync(org, userId, email, ct);
                actorUserId = userId;
            }

            await _tenantUserService.RecordAuditEventAsync(
                org, Api.Constants.SecurityAuditActions.SessionSignedIn, actorUserId,
                targetType: "tenant", targetId: tenant.TenantId.ToString(), metadata: new { email }, ct: ct);
        }
        catch (Exception ex)
        {
            // Best-effort: auditing must never block or fail a login.
            _logger.LogWarning(ex, "Failed to record sign-in audit event for user {UserId}", userId);
        }
    }
}
