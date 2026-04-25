using Api.Services.BffSession;

namespace Orkyo.Foundation.Tests.Services;

public class InMemoryBffSessionStoreTests
{
    private readonly InMemoryBffSessionStore _store =
        new(new Mock<Microsoft.Extensions.Logging.ILogger<InMemoryBffSessionStore>>().Object);

    private static BffSessionRecord CreateSession(string? sessionId = null, DateTimeOffset? expiresAt = null) =>
        new()
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString("N"),
            UserId = Guid.NewGuid().ToString(),
            ExternalSubject = "ext-sub-123",
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            IdToken = "id-token",
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(8),
            TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
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
        var session = CreateSession(expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
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
    public async Task Remove_NoErrorWhenNotFound() =>
        await _store.RemoveAsync("nonexistent");

    [Fact]
    public async Task RefreshTokens_UpdatesTokensAndExpiry()
    {
        var session = CreateSession();
        await _store.SetAsync(session);
        var newExpiry = DateTimeOffset.UtcNow.AddHours(1);
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
        await _store.RefreshTokensAsync("nonexistent", "a", "b", DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public async Task Get_PurgesExpiredSessions()
    {
        var expired = CreateSession("expired-id", DateTimeOffset.UtcNow.AddSeconds(-1));
        var valid = CreateSession("valid-id", DateTimeOffset.UtcNow.AddHours(1));
        await _store.SetAsync(expired);
        await _store.SetAsync(valid);
        (await _store.GetAsync(valid.SessionId)).Should().NotBeNull();
        (await _store.GetAsync(expired.SessionId)).Should().BeNull();
    }
}
