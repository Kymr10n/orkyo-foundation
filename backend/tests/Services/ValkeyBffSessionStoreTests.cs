using Api.Services.BffSession;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The per-user index that lets "log out everywhere" find a user's sessions. No Valkey runs
/// under the test host, so the database is a mock and the tests assert the commands sent.
/// </summary>
public class ValkeyBffSessionStoreTests
{
    private readonly Mock<IDatabase> _db = new();
    private readonly ValkeyBffSessionStore _store;

    public ValkeyBffSessionStoreTests()
    {
        var valkey = new Mock<IConnectionMultiplexer>();
        valkey.Setup(v => v.GetDatabase(It.IsAny<int>(), It.IsAny<object?>())).Returns(_db.Object);
        _store = new ValkeyBffSessionStore(valkey.Object, NullLogger<ValkeyBffSessionStore>.Instance, TimeProvider.System);
    }

    [Fact]
    public async Task Set_IndexesTheSessionUnderItsUser()
    {
        var session = new BffSessionRecord
        {
            SessionId = "session-aaaaaaaa",
            UserId = "user-1",
            ExternalSubject = "sub",
            AccessToken = "a",
            RefreshToken = "r",
            IdToken = "i",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _store.SetAsync(session);

        _db.Verify(d => d.SetAddAsync("bff:u:user-1", "session-aaaaaaaa", It.IsAny<CommandFlags>()), Times.Once);
        _db.Verify(d => d.KeyExpireAsync("bff:u:user-1", It.IsAny<TimeSpan?>(), ExpireWhen.HasNoExpiry,
            It.IsAny<CommandFlags>()), Times.Once);
        _db.Verify(d => d.KeyExpireAsync("bff:u:user-1", It.IsAny<TimeSpan?>(), ExpireWhen.GreaterThanCurrentExpiry,
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAllForUser_DeletesEveryIndexedSessionAndTheIndex()
    {
        _db.Setup(d => d.SetMembersAsync("bff:u:user-1", It.IsAny<CommandFlags>()))
            .ReturnsAsync([(RedisValue)"s1", (RedisValue)"s2"]);
        RedisKey[]? deleted = null;
        _db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
            .Callback<RedisKey[], CommandFlags>((keys, _) => deleted = keys)
            .ReturnsAsync(3);

        await _store.RemoveAllForUserAsync("user-1");

        deleted!.Select(k => k.ToString()).Should().BeEquivalentTo("bff:s:s1", "bff:s:s2", "bff:u:user-1");
    }

    [Fact]
    public async Task RemoveAllForUser_DoesNotSwallowAStoreFailure()
    {
        // "Signed out everywhere" must not be reported when the store could not be reached.
        _db.Setup(d => d.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("down"));

        var act = () => _store.RemoveAllForUserAsync("user-1");

        await act.Should().ThrowAsync<RedisException>();
    }
}
