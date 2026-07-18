namespace Api.Services;

/// <summary>
/// No-op <see cref="IBreakGlassSessionStore"/> for editions with no tenant boundaries
/// to cross (single-tenant Community). Break-glass is a multi-tenant concept — an admin
/// crossing into a tenant — so every operation is a no-op. Mirrors how
/// <see cref="Api.Security.Quotas.NoOpQuotaEnforcer"/> lets Community consume a shared
/// null object from foundation instead of writing its own.
/// </summary>
public sealed class NullBreakGlassSessionStore : IBreakGlassSessionStore
{
    public BreakGlassSession? Create(Guid adminId, string tenantSlug, string reason, TimeSpan? duration = null)
        => null;

    public bool HasActiveSession(Guid adminId, string tenantSlug)
        => false;

    public BreakGlassSession? GetActiveSession(Guid adminId, string tenantSlug)
        => null;

    public RenewResult TryRenew(string sessionId, Guid adminId, TimeSpan? extension = null)
        => RenewResult.NotFound();

    public BreakGlassSession? TryRevoke(string sessionId, Guid adminId)
        => null;
}
