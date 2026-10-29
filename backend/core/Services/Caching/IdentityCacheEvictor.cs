using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Api.Services.Caching;

/// <summary>
/// Evicts an identity entry (<see cref="IdentityCacheKeys"/>) in every process, not only this one.
/// </summary>
/// <remarks>
/// <see cref="SingleFlightCache"/> is per process: after a role change, a removal or a disable,
/// the other API slot (and any later second API host) kept the stale role for the whole TTL.
/// The eviction is removed locally and published on Valkey; every API process's
/// <see cref="IdentityCacheEvictionSubscriber"/> removes the same key. Without Valkey (a test
/// host) it stays local, and the TTL remains the backstop when a publish is lost.
/// </remarks>
public sealed class IdentityCacheEvictor(
    SingleFlightCache cache,
    ILogger<IdentityCacheEvictor> logger,
    IConnectionMultiplexer? valkey = null)
{
    public static readonly RedisChannel Channel = RedisChannel.Literal("orkyo:identity-cache:evict");

    /// <summary>Evicts a user's cached role in one tenant, everywhere.</summary>
    public void EvictRole(Guid userId, Guid tenantId) => Evict(IdentityCacheKeys.Role(userId, tenantId));

    /// <summary>
    /// Evicts the cached DB principal of an external subject, everywhere. A disabled user must
    /// stop resolving on the tenant-less routes too, not only lose their tenant roles.
    /// </summary>
    public void EvictPrincipal(string externalSubject) => Evict(IdentityCacheKeys.Principal(externalSubject));

    private void Evict(string key)
    {
        cache.Remove(key);
        if (valkey is null)
            return;
        try
        {
            valkey.GetSubscriber().Publish(Channel, key, CommandFlags.FireAndForget);
        }
        catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "Identity cache eviction of {Key} not published; other processes keep it until its TTL", key);
        }
    }

    /// <summary>A key another process evicted. Only identity keys are accepted from the channel.</summary>
    internal void OnRemoteEviction(string? key)
    {
        if (key is not null && key.StartsWith(IdentityCacheKeys.Prefix, StringComparison.Ordinal))
            cache.Remove(key);
    }
}

/// <summary>Applies the evictions other processes publish (<see cref="IdentityCacheEvictor"/>).</summary>
public sealed class IdentityCacheEvictionSubscriber(
    IdentityCacheEvictor evictor,
    ILogger<IdentityCacheEvictionSubscriber> logger,
    IConnectionMultiplexer? valkey = null) : IHostedService
{
    private int _subscribed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (valkey is null)
            return;
        // A failed first attempt is retried when the client reconnects: a process that never
        // subscribed would otherwise serve stale roles for its whole lifetime (StackExchange.Redis
        // re-subscribes on its own only for a subscription that was registered). Startup itself is
        // not failed — a Valkey restart during a slot start would then take the API down with it.
        valkey.ConnectionRestored += async (_, _) => await TrySubscribeAsync();
        await TrySubscribeAsync();
    }

    private async Task TrySubscribeAsync()
    {
        if (Interlocked.CompareExchange(ref _subscribed, 1, 0) != 0)
            return;
        try
        {
            await valkey!.GetSubscriber().SubscribeAsync(IdentityCacheEvictor.Channel, (_, message) => evictor.OnRemoteEviction(message));
            logger.LogInformation("Subscribed to identity cache evictions");
        }
        catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
        {
            Interlocked.Exchange(ref _subscribed, 0);
            logger.LogWarning(ex, "Identity cache eviction channel not subscribed; retried when Valkey reconnects, cached roles expire by TTL until then");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (valkey is null)
            return;
        try
        {
            await valkey.GetSubscriber().UnsubscribeAsync(IdentityCacheEvictor.Channel);
        }
        catch (Exception ex) when (ex is RedisException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "Identity cache eviction channel unsubscribe failed at shutdown");
        }
    }
}
