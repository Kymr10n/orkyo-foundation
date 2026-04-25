using System.Text.Json;
using StackExchange.Redis;

namespace Api.Services.BffSession;

/// <summary>
/// Redis-backed implementation of <see cref="IBffSessionStore"/>.
/// Safe for multi-instance / horizontally-scaled deployments.
/// Key: <c>bff:s:{sessionId}</c> with TTL = session duration.
/// </summary>
public sealed class RedisBffSessionStore : IBffSessionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBffSessionStore> _logger;

    private static string SessionKey(string sessionId) => $"bff:s:{sessionId}";

    public RedisBffSessionStore(
        IConnectionMultiplexer redis,
        ILogger<RedisBffSessionStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<BffSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = (string?)await db.StringGetAsync(SessionKey(sessionId));
            if (string.IsNullOrEmpty(json))
                return null;

            var session = JsonSerializer.Deserialize<BffSessionRecord>(json);
            if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
                return null;

            // Update last activity (fire-and-forget — not critical)
            session.LastActivityAt = DateTimeOffset.UtcNow;
            _ = db.StringSetAsync(SessionKey(sessionId), JsonSerializer.Serialize(session),
                session.ExpiresAt - DateTimeOffset.UtcNow, flags: CommandFlags.FireAndForget);

            return session;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error getting BFF session SessionId={SessionIdPrefix}…",
                sessionId[..Math.Min(8, sessionId.Length)]);
            return null;
        }
    }

    public async Task SetAsync(BffSessionRecord session, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var ttl = session.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero)
                return;

            var json = JsonSerializer.Serialize(session);
            await db.StringSetAsync(SessionKey(session.SessionId), json, ttl);

            _logger.LogDebug("BFF session stored in Redis: SessionId={SessionIdPrefix}…", session.SessionId[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error storing BFF session SessionId={SessionIdPrefix}…",
                session.SessionId[..Math.Min(8, session.SessionId.Length)]);
        }
    }

    public async Task RemoveAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(SessionKey(sessionId));
            _logger.LogDebug("BFF session removed from Redis: SessionId={SessionIdPrefix}…", sessionId[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error removing BFF session SessionId={SessionIdPrefix}…",
                sessionId[..Math.Min(8, sessionId.Length)]);
        }
    }

    public async Task RefreshTokensAsync(string sessionId, string accessToken, string refreshToken, DateTimeOffset tokenExpiresAt, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = (string?)await db.StringGetAsync(SessionKey(sessionId));
            if (string.IsNullOrEmpty(json))
                return;

            var session = JsonSerializer.Deserialize<BffSessionRecord>(json);
            if (session is null)
                return;

            var updated = session with
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                TokenExpiresAt = tokenExpiresAt,
                LastActivityAt = DateTimeOffset.UtcNow,
            };

            // Keep Redis TTL based on overall session expiry, not token expiry
            var ttl = updated.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero)
                return;

            await db.StringSetAsync(SessionKey(session.SessionId), JsonSerializer.Serialize(updated), ttl);
            _logger.LogDebug("BFF session tokens refreshed in Redis: SessionId={SessionIdPrefix}…", sessionId[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error refreshing BFF session tokens SessionId={SessionIdPrefix}…",
                sessionId[..Math.Min(8, sessionId.Length)]);
        }
    }
}
