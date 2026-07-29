namespace Api.Services.BffSession;

/// <summary>
/// Represents a BFF authentication session with encrypted OIDC tokens.
/// </summary>
public sealed record BffSessionRecord
{
    public required string SessionId { get; init; }
    public required string UserId { get; init; }
    public required string ExternalSubject { get; init; }
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required string IdToken { get; init; }
    /// <summary>
    /// When the session expires if nothing else happens — the idle deadline. Slid forward on
    /// activity when <see cref="SlidingEnabled"/>, never past <see cref="AbsoluteExpiresAt"/>.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Hard ceiling from login that sliding can never pass. <c>default</c> on sessions written
    /// before sliding existed — treated as "not sliding", so they expire exactly as before.
    /// </summary>
    public DateTimeOffset AbsoluteExpiresAt { get; init; }

    /// <summary>
    /// Whether activity extends <see cref="ExpiresAt"/>. False for ephemeral sessions such as
    /// the SaaS demo, whose short window is a deliberate limit on anonymous access and must
    /// not be extendable by simply staying active.
    /// </summary>
    public bool SlidingEnabled { get; init; }
    /// <summary>When the current access token expires (based on KC's expires_in, e.g. 5m). Used to trigger proactive token refresh.</summary>
    public DateTimeOffset TokenExpiresAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; set; }
    /// <summary>
    /// Name of the OAuth client this session's tokens were issued to, resolved to
    /// credentials by <c>IBffAuthClientRegistry</c> at refresh time. Null (the
    /// default, and the value for every session predating this field) means the
    /// primary backend client. A refresh_token grant MUST present the credentials
    /// of the issuing client — Keycloak rejects a refresh token issued to one
    /// client when presented with another's credentials.
    /// </summary>
    public string? AuthClient { get; init; }
}

/// <summary>
/// Storage abstraction for BFF sessions.
/// Implementations must be thread-safe and suitable for the deployment topology:
/// use <see cref="InMemoryBffSessionStore"/> for single-instance dev/test,
/// <see cref="ValkeyBffSessionStore"/> for multi-instance production deployments.
/// </summary>
public interface IBffSessionStore
{
    /// <summary>Retrieves a session by ID, or null if not found or expired.</summary>
    Task<BffSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Stores or updates a session.</summary>
    Task SetAsync(BffSessionRecord session, CancellationToken ct = default);

    /// <summary>Removes a session by ID.</summary>
    Task RemoveAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Updates the tokens and token expiry for an existing session (after token refresh).</summary>
    Task RefreshTokensAsync(string sessionId, string accessToken, string refreshToken, DateTimeOffset tokenExpiresAt, CancellationToken ct = default);

    /// <summary>
    /// Slides the idle deadline forward and extends the stored record's lifetime to match.
    /// Callers must clamp <paramref name="expiresAt"/> to the session's absolute cap.
    /// </summary>
    Task SlideExpiryAsync(string sessionId, DateTimeOffset expiresAt, CancellationToken ct = default);

    /// <summary>
    /// Atomically acquires a short-lived per-session refresh lock so that a burst of concurrent
    /// requests performs at most one token refresh. Returns <c>true</c> if this caller won the lock
    /// and should refresh; <c>false</c> if another caller/instance already holds it. The lock
    /// auto-expires after <paramref name="ttl"/> (no explicit release) so a crashed refresher can't
    /// block future refreshes. Must be atomic and coordinate across instances in the production store.
    /// </summary>
    Task<bool> TryAcquireRefreshLockAsync(string sessionId, TimeSpan ttl, CancellationToken ct = default);
}
