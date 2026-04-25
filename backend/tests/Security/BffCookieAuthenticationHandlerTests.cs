using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Api.Configuration;
using Api.Security;
using Api.Services.BffSession;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Orkyo.Shared.Keycloak;

namespace Orkyo.Foundation.Tests.Security;

public class BffCookieAuthenticationHandlerTests
{
    private const string CookieName = "orkyo-session";
    private const string TestSessionId = "test-session-id-12345678";

    private readonly Mock<IBffSessionStore> _sessionStore = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly IDataProtectionProvider _dataProtectionProvider = DataProtectionProvider.Create("BffCookieTests");
    private readonly IDataProtector _protector;
    private readonly BffOptions _bffOptions = new() { CookieName = CookieName };
    private readonly KeycloakOptions _keycloakOptions = new()
    {
        BaseUrl = "https://auth.orkyo.com", Realm = "orkyo",
        BackendClientId = "orkyo-backend", BackendClientSecret = "test-secret",
    };

    public BffCookieAuthenticationHandlerTests()
    {
        _protector = _dataProtectionProvider.CreateProtector("BffSession");
    }

    private static string CreateTestJwt(DateTimeOffset? expires = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var claims = new List<Claim>
        {
            new("sub", "user-123"), new("preferred_username", "alex"),
            new("email", "alex@orkyo.com"), new("realm_access.roles", "admin"),
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = (expires ?? DateTimeOffset.UtcNow.AddHours(1)).UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(new byte[32]), SecurityAlgorithms.HmacSha256Signature),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private string EncryptSessionId(string sessionId) => _protector.Protect(sessionId);

    private BffSessionRecord CreateSession(string? accessToken = null, DateTimeOffset? tokenExpiresAt = null) =>
        new()
        {
            SessionId = TestSessionId, UserId = "user-123", ExternalSubject = "ext-sub-123",
            AccessToken = accessToken ?? CreateTestJwt(), RefreshToken = "refresh-token-value",
            IdToken = "id-token-value", ExpiresAt = DateTimeOffset.UtcNow.AddHours(8),
            TokenExpiresAt = tokenExpiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5),
            CreatedAt = DateTimeOffset.UtcNow, LastActivityAt = DateTimeOffset.UtcNow,
        };

    private async Task<AuthenticateResult> RunAuthenticateAsync(HttpContext httpContext)
    {
        var scheme = new AuthenticationScheme(BffCookieAuthenticationHandler.SchemeName, null, typeof(BffCookieAuthenticationHandler));
        var optionsMonitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        optionsMonitor.Setup(m => m.Get(BffCookieAuthenticationHandler.SchemeName)).Returns(new AuthenticationSchemeOptions());
        var handler = new BffCookieAuthenticationHandler(
            optionsMonitor.Object, NullLoggerFactory.Instance, UrlEncoder.Default,
            _sessionStore.Object, _dataProtectionProvider, Options.Create(_bffOptions),
            _keycloakOptions, _httpClientFactory.Object);
        await handler.InitializeAsync(scheme, httpContext);
        return await handler.AuthenticateAsync();
    }

    private static DefaultHttpContext CreateHttpContext(string? cookieName = null, string? cookieValue = null)
    {
        var context = new DefaultHttpContext();
        if (cookieName is not null && cookieValue is not null)
            context.Request.Headers.Cookie = $"{cookieName}={cookieValue}";
        return context;
    }

    [Fact]
    public async Task NoCookie_ReturnsNoResult()
    {
        var result = await RunAuthenticateAsync(CreateHttpContext());
        result.None.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidCookie_DecryptionFails_ReturnsFailWithMessage()
    {
        var result = await RunAuthenticateAsync(CreateHttpContext(CookieName, "not-a-valid-encrypted-value"));
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Be("Invalid session cookie");
    }

    [Fact]
    public async Task SessionNotFound_ReturnsFailWithMessage()
    {
        _sessionStore.Setup(s => s.GetAsync(TestSessionId, It.IsAny<CancellationToken>())).ReturnsAsync((BffSessionRecord?)null);
        var result = await RunAuthenticateAsync(CreateHttpContext(CookieName, EncryptSessionId(TestSessionId)));
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Be("Session not found or expired");
    }

    [Fact]
    public async Task ValidSession_ReturnsSuccessWithCorrectClaims()
    {
        _sessionStore.Setup(s => s.GetAsync(TestSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateSession());
        var result = await RunAuthenticateAsync(CreateHttpContext(CookieName, EncryptSessionId(TestSessionId)));
        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirst("sub")!.Value.Should().Be("user-123");
        result.Principal.FindFirst("email")!.Value.Should().Be("alex@orkyo.com");
    }

    [Fact]
    public async Task ValidSession_FiltersOutInternalJwtClaims()
    {
        _sessionStore.Setup(s => s.GetAsync(TestSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateSession());
        var result = await RunAuthenticateAsync(CreateHttpContext(CookieName, EncryptSessionId(TestSessionId)));
        result.Succeeded.Should().BeTrue();
        var types = result.Principal!.Claims.Select(c => c.Type).ToList();
        types.Should().NotContain("nbf").And.NotContain("jti").And.NotContain("iat").And.NotContain("exp");
    }

    [Fact]
    public async Task ValidSession_ClaimsIdentityUsesCorrectSchemeAndClaimTypes()
    {
        _sessionStore.Setup(s => s.GetAsync(TestSessionId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateSession());
        var result = await RunAuthenticateAsync(CreateHttpContext(CookieName, EncryptSessionId(TestSessionId)));
        result.Ticket!.AuthenticationScheme.Should().Be("BffCookie");
        var identity = (ClaimsIdentity)result.Principal!.Identity!;
        identity.NameClaimType.Should().Be("preferred_username");
        identity.RoleClaimType.Should().Be("realm_access.roles");
    }

    [Fact]
    public async Task TokenNotNearExpiry_DoesNotTriggerRefresh()
    {
        _sessionStore.Setup(s => s.GetAsync(TestSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSession(tokenExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10)));
        await RunAuthenticateAsync(CreateHttpContext(CookieName, EncryptSessionId(TestSessionId)));
        _httpClientFactory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvalidStoredAccessToken_ReturnsFailure()
    {
        _sessionStore.Setup(s => s.GetAsync(TestSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSession("not-a-valid-jwt", DateTimeOffset.UtcNow.AddMinutes(10)));
        var result = await RunAuthenticateAsync(CreateHttpContext(CookieName, EncryptSessionId(TestSessionId)));
        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Be("Invalid stored access token");
    }
}
