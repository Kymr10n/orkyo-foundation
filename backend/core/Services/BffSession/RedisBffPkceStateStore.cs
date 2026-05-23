using System.Text.Json;
using StackExchange.Redis;

namespace Api.Services.BffSession;

/// <summary>
/// Redis-backed PKCE state store for multi-instance deployments.
/// Uses the GETDEL command (Redis ≥ 6.2) for an atomic get-and-delete,
/// eliminating the TOCTOU race condition present in a two-step GET + DEL.
/// Key: <c>bff:pkce:{state}</c> with TTL = StateTtl (10 minutes).
/// </summary>
public sealed class RedisBffPkceStateStore : IBffPkceStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBffPkceStateStore> _logger;

    private static string StateKey(string state) => $"bff:pkce:{state}";

    public RedisBffPkceStateStore(
        IConnectionMultiplexer redis,
        ILogger<RedisBffPkceStateStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task SetAsync(string state, PkceStateData data, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(data);
            await db.StringSetAsync(StateKey(state), json, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error storing PKCE state");
            throw;
        }
    }

    public async Task<PkceStateData?> GetAndRemoveAsync(string state, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();

            // GETDEL is atomic: retrieves and deletes in a single round-trip.
            // A second call with the same key always returns nil — prevents replay.
            var json = (string?)await db.StringGetDeleteAsync(StateKey(state));
            if (string.IsNullOrEmpty(json))
                return null;

            return JsonSerializer.Deserialize<PkceStateData>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis error retrieving PKCE state");
            return null;
        }
    }
}
