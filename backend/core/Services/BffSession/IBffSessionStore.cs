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
    /// <summary>When the overall BFF session expires (based on SessionDuration, e.g. 8h).</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>When the current access token expires (based on KC's expires_in, e.g. 5m). Used to trigger proactive token refresh.</summary>
    public DateTimeOffset TokenExpiresAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset LastActivityAt { get; set; }
}

/// <summary>
/// Storage abstraction for BFF sessions.
/// Implementations must be thread-safe and suitable for the deployment topology:
/// use <see cref="InMemoryBffSessionStore"/> for single-instance dev/test,
/// <see cref="RedisBffSessionStore"/> for multi-instance production deployments.
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
}
