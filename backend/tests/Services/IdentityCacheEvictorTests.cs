using Api.Services.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Cross-process eviction of the cached tenant role. No Valkey runs under the test host, so the
/// channel is a fake that delivers each publish to every subscribed handler — two evictors on it
/// stand for two API processes.
/// </summary>
public class IdentityCacheEvictorTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly string RoleKey = IdentityCacheKeys.Role(UserId, TenantId);

    private sealed class FakeChannel
    {
        public readonly List<Action<RedisChannel, RedisValue>> Handlers = [];
        public readonly Mock<IConnectionMultiplexer> Valkey = new();

        public FakeChannel()
        {
            var subscriber = new Mock<ISubscriber>();
            subscriber
                .Setup(s => s.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
                .Callback<RedisChannel, RedisValue, CommandFlags>((channel, message, _) =>
                {
                    foreach (var handler in Handlers.ToList()) handler(channel, message);
                })
                .Returns(1);
            subscriber
                .Setup(s => s.SubscribeAsync(It.IsAny<RedisChannel>(), It.IsAny<Action<RedisChannel, RedisValue>>(), It.IsAny<CommandFlags>()))
                .Callback<RedisChannel, Action<RedisChannel, RedisValue>, CommandFlags>((_, handler, _) => Handlers.Add(handler))
                .Returns(Task.CompletedTask);
            Valkey.Setup(v => v.GetSubscriber(It.IsAny<object?>())).Returns(subscriber.Object);
        }
    }

    private static (SingleFlightCache Cache, IdentityCacheEvictor Evictor) Process(IConnectionMultiplexer? valkey)
    {
        var cache = new SingleFlightCache(new MemoryCache(new MemoryCacheOptions()));
        return (cache, new IdentityCacheEvictor(cache, NullLogger<IdentityCacheEvictor>.Instance, valkey));
    }

    private static async Task SubscribeAsync(IdentityCacheEvictor evictor, IConnectionMultiplexer valkey) =>
        await new IdentityCacheEvictionSubscriber(evictor, NullLogger<IdentityCacheEvictionSubscriber>.Instance, valkey)
            .StartAsync(CancellationToken.None);

    [Fact]
    public async Task EvictRole_RemovesTheEntryInAnotherProcess()
    {
        var channel = new FakeChannel();
        var (cacheA, evictorA) = Process(channel.Valkey.Object);
        var (cacheB, evictorB) = Process(channel.Valkey.Object);
        await SubscribeAsync(evictorA, channel.Valkey.Object);
        await SubscribeAsync(evictorB, channel.Valkey.Object);
        cacheA.Set(RoleKey, "admin", TimeSpan.FromMinutes(5));
        cacheB.Set(RoleKey, "admin", TimeSpan.FromMinutes(5));

        evictorA.EvictRole(UserId, TenantId);

        cacheA.TryGet<string>(RoleKey, out _).Should().BeFalse();
        cacheB.TryGet<string>(RoleKey, out _).Should().BeFalse("the other API process must drop the stale role too");
    }

    [Fact]
    public async Task ARemoteMessageOutsideTheIdentityPrefix_IsIgnored()
    {
        var channel = new FakeChannel();
        var (cache, evictor) = Process(channel.Valkey.Object);
        await SubscribeAsync(evictor, channel.Valkey.Object);
        cache.Set("analytics:tenant:1", "payload", TimeSpan.FromMinutes(5));

        foreach (var handler in channel.Handlers) handler(IdentityCacheEvictor.Channel, "analytics:tenant:1");

        cache.TryGet<string>("analytics:tenant:1", out _).Should().BeTrue("the channel only evicts identity keys");
    }

    [Fact]
    public void WithoutValkey_EvictRole_IsLocal()
    {
        var (cache, evictor) = Process(valkey: null);
        cache.Set(RoleKey, "admin", TimeSpan.FromMinutes(5));

        evictor.EvictRole(UserId, TenantId);

        cache.TryGet<string>(RoleKey, out _).Should().BeFalse();
    }

    [Fact]
    public void APublishFailure_StillEvictsLocally()
    {
        var subscriber = new Mock<ISubscriber>();
        subscriber
            .Setup(s => s.Publish(It.IsAny<RedisChannel>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
            .Throws(new RedisException("down"));
        var valkey = new Mock<IConnectionMultiplexer>();
        valkey.Setup(v => v.GetSubscriber(It.IsAny<object?>())).Returns(subscriber.Object);
        var (cache, evictor) = Process(valkey.Object);
        cache.Set(RoleKey, "admin", TimeSpan.FromMinutes(5));

        var act = () => evictor.EvictRole(UserId, TenantId);

        act.Should().NotThrow("a role write must not fail because Valkey is down");
        cache.TryGet<string>(RoleKey, out _).Should().BeFalse();
    }
}
