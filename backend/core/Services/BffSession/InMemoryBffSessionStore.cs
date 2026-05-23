using System.Collections.Concurrent;

namespace Api.Services.BffSession;

/// <summary>
/// In-memory implementation of <see cref="IBffSessionStore"/>.
/// Suitable for single-instance deployments (development / test).
/// For multi-instance production deployments use <see cref="RedisBffSessionStore"/>.
/// </summary>
public sealed class InMemoryBffSessionStore : IBffSessionStore
{
    private readonly ConcurrentDictionary<string, BffSessionRecord> _sessions = new();
    private readonly ILogger<InMemoryBffSessionStore> _logger;

    public InMemoryBffSessionStore(ILogger<InMemoryBffSessionStore> logger)
    {
        _logger = logger;
    }

    public Task<BffSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        PurgeExpired();

        if (!_sessions.TryGetValue(sessionId, out var session))
            return Task.FromResult<BffSessionRecord?>(null);

        if (session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(sessionId, out _);
            return Task.FromResult<BffSessionRecord?>(null);
        }

        session.LastActivityAt = DateTimeOffset.UtcNow;
        return Task.FromResult<BffSessionRecord?>(session);
    }

    public Task SetAsync(BffSessionRecord session, CancellationToken ct = default)
    {
        _sessions[session.SessionId] = session;
        _logger.LogDebug("BFF session stored: SessionId={SessionIdPrefix}…", session.SessionId[..8]);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryRemove(sessionId, out _))
            _logger.LogDebug("BFF session removed: SessionId={SessionIdPrefix}…", sessionId[..8]);

        return Task.CompletedTask;
    }

    public Task RefreshTokensAsync(string sessionId, string accessToken, string refreshToken, DateTimeOffset tokenExpiresAt, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var existing))
            return Task.CompletedTask;

        var updated = existing with
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenExpiresAt = tokenExpiresAt,
            LastActivityAt = DateTimeOffset.UtcNow,
        };

        _sessions[sessionId] = updated;
        _logger.LogDebug("BFF session tokens refreshed: SessionId={SessionIdPrefix}…", sessionId[..8]);
        return Task.CompletedTask;
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _sessions.Where(kvp => kvp.Value.ExpiresAt <= now).Select(kvp => kvp.Key).ToList())
            _sessions.TryRemove(key, out _);
    }
}
