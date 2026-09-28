using Api.Services.BffSession;
using Microsoft.Extensions.Time.Testing;

namespace Orkyo.Foundation.Tests.Services;

public class InMemoryBffSessionStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2031, 3, 1, 8, 0, 0, TimeSpan.Zero));
    private readonly InMemoryBffSessionStore _store;

    public InMemoryBffSessionStoreTests() =>
        _store = new(new Mock<Microsoft.Extensions.Logging.ILogger<InMemoryBffSessionStore>>().Object, _time);

    private BffSessionRecord CreateSession(string? sessionId = null, DateTimeOffset? expiresAt = null) =>
        new()
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString("N"),
            UserId = Guid.NewGuid().ToString(),
            ExternalSubject = "ext-sub-123",
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            IdToken = "id-token",
            ExpiresAt = expiresAt ?? _time.GetUtcNow().AddHours(8),
            TokenExpiresAt = _time.GetUtcNow().AddMinutes(5),
            CreatedAt = _time.GetUtcNow(),
        };

    [Fact]
    public async Task SetAndGet_RoundTrip()
    {
        var session = CreateSession();
        await _store.SetAsync(session);
        var result = await _store.GetAsync(session.SessionId);
        result.Should().NotBeNull();
        result!.SessionId.Should().Be(session.SessionId);
        result.UserId.Should().Be(session.UserId);
        result.AccessToken.Should().Be(session.AccessToken);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenNotFound() =>
        (await _store.GetAsync("nonexistent")).Should().BeNull();

    [Fact]
    public async Task Get_ReturnsNull_WhenExpired()
    {
        var session = CreateSession(expiresAt: _time.GetUtcNow().AddSeconds(-1));
        await _store.SetAsync(session);
        (await _store.GetAsync(session.SessionId)).Should().BeNull();
    }

    [Fact]
    public async Task Remove_DeletesSession()
    {
        var session = CreateSession();
        await _store.SetAsync(session);
        await _store.RemoveAsync(session.SessionId);
        (await _store.GetAsync(session.SessionId)).Should().BeNull();
    }

    [Fact]
    public async Task RemoveAllForUser_RemovesOnlyThatUsersSessions()
    {
        var mine1 = CreateSession();
        var mine2 = CreateSession() with { UserId = mine1.UserId };
        var theirs = CreateSession();
        await _store.SetAsync(mine1);
        await _store.SetAsync(mine2);
        await _store.SetAsync(theirs);

        await _store.RemoveAllForUserAsync(mine1.UserId);

        (await _store.GetAsync(mine1.SessionId)).Should().BeNull();
        (await _store.GetAsync(mine2.SessionId)).Should().BeNull();
        (await _store.GetAsync(theirs.SessionId)).Should().NotBeNull();
    }

    [Fact]
    public async Task Remove_NoErrorWhenNotFound() =>
        await _store.RemoveAsync("nonexistent");

    [Fact]
    public async Task RefreshTokens_UpdatesTokensAndExpiry()
    {
        var session = CreateSession();
        await _store.SetAsync(session);
        var newExpiry = _time.GetUtcNow().AddHours(1);
        await _store.RefreshTokensAsync(session.SessionId, "new-access", "new-refresh", newExpiry);
        var result = await _store.GetAsync(session.SessionId);
        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("new-access");
        result.RefreshToken.Should().Be("new-refresh");
        result.TokenExpiresAt.Should().Be(newExpiry);
        result.ExpiresAt.Should().Be(session.ExpiresAt);
    }

    [Fact]
    public async Task RefreshTokens_NoOpWhenSessionNotFound() =>
        await _store.RefreshTokensAsync("nonexistent", "a", "b", _time.GetUtcNow().AddHours(1));

    [Fact]
    public async Task Get_PurgesExpiredSessions()
    {
        var expired = CreateSession("expired-id", _time.GetUtcNow().AddSeconds(-1));
        var valid = CreateSession("valid-id", _time.GetUtcNow().AddHours(1));
        await _store.SetAsync(expired);
        await _store.SetAsync(valid);
        (await _store.GetAsync(valid.SessionId)).Should().NotBeNull();
        (await _store.GetAsync(expired.SessionId)).Should().BeNull();
    }

    // ── Refresh lock (single-flight) ───────────────────────────────────────

    [Fact]
    public async Task TryAcquireRefreshLock_FirstCallerWins_SecondIsBlocked()
    {
        (await _store.TryAcquireRefreshLockAsync("s1", TimeSpan.FromSeconds(30))).Should().BeTrue();
        (await _store.TryAcquireRefreshLockAsync("s1", TimeSpan.FromSeconds(30))).Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireRefreshLock_DifferentSessionsAreIndependent()
    {
        (await _store.TryAcquireRefreshLockAsync("s1", TimeSpan.FromSeconds(30))).Should().BeTrue();
        (await _store.TryAcquireRefreshLockAsync("s2", TimeSpan.FromSeconds(30))).Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireRefreshLock_ReacquirableAfterTtlExpires()
    {
        (await _store.TryAcquireRefreshLockAsync("s1", TimeSpan.FromMilliseconds(20))).Should().BeTrue();
        _time.Advance(TimeSpan.FromMilliseconds(40));
        (await _store.TryAcquireRefreshLockAsync("s1", TimeSpan.FromSeconds(30))).Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireRefreshLock_ConcurrentBurst_ExactlyOneWinner()
    {
        // The People-tab site-switch scenario: a burst of parallel requests for one session must
        // yield exactly one refresh lock holder, so only one refresh_token grant is issued.
        var tasks = Enumerable.Range(0, 20)
            .Select(_ => _store.TryAcquireRefreshLockAsync("burst", TimeSpan.FromSeconds(30)))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        results.Count(won => won).Should().Be(1);
    }
}
