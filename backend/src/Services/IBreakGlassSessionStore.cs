namespace Api.Services;

/// <summary>
/// Represents an active break-glass session.
/// <see cref="CreatedAt"/> anchors the absolute hard cap: renewals extend
/// <see cref="ExpiresAt"/> but never past <c>CreatedAt + BreakGlassSessionAbsoluteCap</c>.
/// </summary>
public record BreakGlassSession(
    string SessionId,
    Guid AdminId,
    string TenantSlug,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Storage abstraction for break-glass sessions.
/// Implementations must be thread-safe and suitable for the deployment topology:
/// use <see cref="InMemoryBreakGlassSessionStore"/> for single-instance dev/test,
/// <see cref="RedisBreakGlassSessionStore"/> for multi-instance production deployments.
/// </summary>
public interface IBreakGlassSessionStore
{
    /// <summary>
    /// Creates a new break-glass session.
    /// Returns <c>null</c> if the per-admin concurrent session cap is already reached.
    /// </summary>
    BreakGlassSession? Create(Guid adminId, string tenantSlug, string reason, TimeSpan? duration = null);

    /// <summary>Returns true if the admin has an active, non-expired session for the tenant.</summary>
    bool HasActiveSession(Guid adminId, string tenantSlug);

    /// <summary>
    /// Retrieves the active session (if any) this admin holds for the given tenant.
    /// Returns <c>null</c> if no active session exists.
    /// </summary>
    BreakGlassSession? GetActiveSession(Guid adminId, string tenantSlug);

    /// <summary>
    /// Extends the session's expiry by <paramref name="extension"/> (default:
    /// <see cref="Orkyo.Shared.TimePolicyConstants.BreakGlassSessionDefaultDuration"/>).
    /// Enforces the absolute hard cap anchored at <see cref="BreakGlassSession.CreatedAt"/>.
    /// </summary>
    /// <returns>
    /// The renewed session, or a <see cref="RenewResult"/> with the reason renewal was refused
    /// (not found, wrong owner, hard-cap reached).
    /// </returns>
    RenewResult TryRenew(string sessionId, Guid adminId, TimeSpan? extension = null);

    /// <summary>
    /// Revokes the session if it exists and belongs to the given admin.
    /// Returns the revoked session (for audit logging) or <c>null</c> if not found or not owned by the caller.
    /// </summary>
    BreakGlassSession? TryRevoke(string sessionId, Guid adminId);
}

/// <summary>
/// Result of a <see cref="IBreakGlassSessionStore.TryRenew"/> attempt. Either the updated session or a failure reason.
/// </summary>
public sealed record RenewResult(BreakGlassSession? Session, RenewFailureReason Reason)
{
    public static RenewResult Success(BreakGlassSession s) => new(s, RenewFailureReason.None);
    public static RenewResult NotFound() => new(null, RenewFailureReason.NotFound);
    public static RenewResult WrongOwner() => new(null, RenewFailureReason.WrongOwner);
    public static RenewResult HardCapReached(BreakGlassSession session) => new(session, RenewFailureReason.HardCapReached);
}

public enum RenewFailureReason
{
    None,
    NotFound,
    WrongOwner,
    HardCapReached,
}
