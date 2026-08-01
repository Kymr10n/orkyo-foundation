using System.Security.Claims;
using Api.Configuration;
using Api.Endpoints;
using Api.Integrations.Keycloak;
using Api.Security;
using Api.Services;
using Api.Services.BffSession;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Orkyo.Foundation.Tests.Security;

/// <summary>
/// The session-creation seam, focused on the expiry envelope it stamps: an ordinary login gets a
/// sliding idle window under an absolute cap; an ephemeral session (the SaaS demo) gets a fixed
/// window that activity cannot renew.
/// </summary>
public class BffSessionEstablisherTests
{
    private readonly Mock<IBffSessionStore> _store = new();
    private readonly Mock<IUserSessionService> _userSessions = new();
    private readonly Mock<IClientIpAccessor> _clientIp = new();
    private readonly Mock<ISignInAuditRecorder> _audit = new();
    private readonly BffOptions _options = new()
    {
        CookieName = "orkyo-session",
        CookieSecure = false,
        SessionIdleDuration = TimeSpan.FromDays(7),
        SessionMaxDuration = TimeSpan.FromDays(14),
    };

    private BffSessionRecord? _stored;

    public BffSessionEstablisherTests()
    {
        _store.Setup(s => s.SetAsync(It.IsAny<BffSessionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<BffSessionRecord, CancellationToken>((r, _) => _stored = r)
            .Returns(Task.CompletedTask);
    }

    private BffSessionEstablisher Create() => new(
        _store.Object,
        DataProtectionProvider.Create("EstablisherTests"),
        _userSessions.Object,
        _clientIp.Object,
        _audit.Object,
        Options.Create(_options),
        NullLogger<BffSessionEstablisher>.Instance);

    private static KeycloakTokenProfile Profile() =>
        KeycloakTokenProfile.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(KeycloakClaims.Subject, "ext-sub-1"),
            new Claim(KeycloakClaims.Email, "u@orkyo.com"),
            new Claim(KeycloakClaims.PreferredUsername, "u"),
        ], "test")));

    private static BffAuthEndpoints.TokenResponse Tokens() => new("at", "rt", "it", 300);

    private async Task EstablishAsync(TimeSpan? lifetime = null, string? authClient = null, bool sliding = true)
    {
        var ctx = new DefaultHttpContext();
        await Create().EstablishAsync(ctx, Guid.NewGuid(), Profile(), Tokens(),
            sessionLifetimeOverride: lifetime, authClient: authClient, slidingEnabled: sliding);
    }

    [Fact]
    public async Task OrdinaryLogin_GetsSlidingIdleWindowUnderAbsoluteCap()
    {
        await EstablishAsync();

        _stored.Should().NotBeNull();
        _stored!.SlidingEnabled.Should().BeTrue();
        // Idle deadline ~7d out, cap ~14d out — the two must differ, or there is nothing to slide into.
        (_stored.ExpiresAt - _stored.CreatedAt).Should().BeCloseTo(TimeSpan.FromDays(7), TimeSpan.FromMinutes(1));
        (_stored.AbsoluteExpiresAt - _stored.CreatedAt).Should().BeCloseTo(TimeSpan.FromDays(14), TimeSpan.FromMinutes(1));
        _stored.AbsoluteExpiresAt.Should().BeAfter(_stored.ExpiresAt);
    }

    [Fact]
    public async Task EphemeralSession_CapCoincidesWithIdleDeadline()
    {
        // The demo shape: 45 minutes, non-sliding. Cap == deadline is what makes the window hard.
        await EstablishAsync(lifetime: TimeSpan.FromMinutes(45), authClient: "demo", sliding: false);

        _stored.Should().NotBeNull();
        _stored!.SlidingEnabled.Should().BeFalse();
        _stored.AuthClient.Should().Be("demo");
        _stored.AbsoluteExpiresAt.Should().Be(_stored.ExpiresAt);
        (_stored.ExpiresAt - _stored.CreatedAt).Should().BeCloseTo(TimeSpan.FromMinutes(45), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LifetimeOverride_DoesNotShortenTheAbsoluteCapOfASlidingSession()
    {
        await EstablishAsync(lifetime: TimeSpan.FromHours(1));

        _stored!.SlidingEnabled.Should().BeTrue();
        (_stored.ExpiresAt - _stored.CreatedAt).Should().BeCloseTo(TimeSpan.FromHours(1), TimeSpan.FromSeconds(5));
        (_stored.AbsoluteExpiresAt - _stored.CreatedAt).Should().BeCloseTo(TimeSpan.FromDays(14), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CookiesAreWrittenWithTheIdleLifetime()
    {
        var ctx = new DefaultHttpContext();
        await Create().EstablishAsync(ctx, Guid.NewGuid(), Profile(), Tokens(),
            sessionLifetimeOverride: TimeSpan.FromMinutes(45), authClient: "demo", slidingEnabled: false);

        var setCookie = ctx.Response.Headers.SetCookie.ToString();
        setCookie.Should().Contain("orkyo-session");
        setCookie.Should().Contain("orkyo-csrf");
        // 45 minutes = 2700s — the max-age observed on demo sessions in production.
        setCookie.Should().Contain("max-age=2700");
    }
}
