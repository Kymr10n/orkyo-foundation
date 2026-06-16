using System.Text.Json;
using StackExchange.Redis;

namespace Api.Services.BffSession;

/// <summary>
/// Valkey-backed implementation of <see cref="IBffSessionStore"/>.
/// Safe for multi-instance / horizontally-scaled deployments.
/// Key: <c>bff:s:{sessionId}</c> with TTL = session duration.
/// </summary>
public sealed class ValkeyBffSessionStore : IBffSessionStore
{
    private readonly IConnectionMultiplexer _valkey;
    private readonly ILogger<ValkeyBffSessionStore> _logger;

    private static string SessionKey(string sessionId) => $"bff:s:{sessionId}";
    private static string RefreshLockKey(string sessionId) => $"bff:refresh-lock:{sessionId}";

    public ValkeyBffSessionStore(
        IConnectionMultiplexer valkey,
        ILogger<ValkeyBffSessionStore> logger)
    {
        _valkey = valkey;
        _logger = logger;
    }

    public async Task<BffSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = _valkey.GetDatabase();
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
            _logger.LogError(ex, "Valkey error getting BFF session SessionId={SessionIdPrefix}…",
                sessionId[..Math.Min(8, sessionId.Length)]);
            return null;
        }
    }

    public async Task SetAsync(BffSessionRecord session, CancellationToken ct = default)
    {
        try
        {
            var db = _valkey.GetDatabase();
            var ttl = session.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero)
                return;

            var json = JsonSerializer.Serialize(session);
            await db.StringSetAsync(SessionKey(session.SessionId), json, ttl);

            _logger.LogDebug("BFF session stored in Valkey: SessionId={SessionIdPrefix}…", session.SessionId[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Valkey error storing BFF session SessionId={SessionIdPrefix}…",
                session.SessionId[..Math.Min(8, session.SessionId.Length)]);
        }
    }

    public async Task RemoveAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var db = _valkey.GetDatabase();
            await db.KeyDeleteAsync(SessionKey(sessionId));
            _logger.LogDebug("BFF session removed from Valkey: SessionId={SessionIdPrefix}…", sessionId[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Valkey error removing BFF session SessionId={SessionIdPrefix}…",
                sessionId[..Math.Min(8, sessionId.Length)]);
        }
    }

    public async Task RefreshTokensAsync(string sessionId, string accessToken, string refreshToken, DateTimeOffset tokenExpiresAt, CancellationToken ct = default)
    {
        try
        {
            var db = _valkey.GetDatabase();
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

            // Keep Valkey TTL based on overall session expiry, not token expiry
            var ttl = updated.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero)
                return;

            await db.StringSetAsync(SessionKey(session.SessionId), JsonSerializer.Serialize(updated), ttl);
            _logger.LogDebug("BFF session tokens refreshed in Valkey: SessionId={SessionIdPrefix}…", sessionId[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Valkey error refreshing BFF session tokens SessionId={SessionIdPrefix}…",
                sessionId[..Math.Min(8, sessionId.Length)]);
        }
    }

    public async Task<bool> TryAcquireRefreshLockAsync(string sessionId, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var db = _valkey.GetDatabase();
            // Atomic SET NX PX — single-flight token refresh across all instances. The key
            // auto-expires after ttl so a crashed/failed refresher can't block future refreshes;
            // no explicit release needed (a successful refresh pushes the token expiry well past
            // the refresh window, so no other request re-enters it before the lock lapses).
            return await db.StringSetAsync(RefreshLockKey(sessionId), "1", ttl, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Valkey error acquiring BFF refresh lock SessionId={SessionIdPrefix}…",
                sessionId[..Math.Min(8, sessionId.Length)]);
            // Fail open: prefer auth continuity over herd-prevention if the store is unreachable.
            return true;
        }
    }
}
