using System.Security.Claims;
using Api.Configuration;
using Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Configuration;

/// <summary>
/// Tests for <see cref="AuthenticationServiceExtensions.AddOrkyoAuthentication"/>.
///
/// Key invariant: RequireHttpsMetadata must be derived from the URL actually fetched
/// for OIDC discovery (MetadataAddress when set, otherwise Authority), not from the
/// public authority alone. This allows the internal container-to-container Keycloak
/// URL to use HTTP while the public authority remains HTTPS.
/// </summary>
public class AuthenticationServiceExtensionsTests
{
    // ── RequireHttpsMetadata — no internal authority ──────────────────────────

    [Fact]
    public void AddOrkyoAuthentication_RequireHttpsMetadataTrue_WhenAuthorityIsHttpsAndNoInternalAuthority()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: null);

        options.RequireHttpsMetadata.Should().BeTrue();
    }

    [Fact]
    public void AddOrkyoAuthentication_RequireHttpsMetadataFalse_WhenAuthorityIsHttpAndNoInternalAuthority()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "http://localhost:8080/realms/orkyo",
            internalAuthority: null);

        options.RequireHttpsMetadata.Should().BeFalse();
    }

    // ── RequireHttpsMetadata — with HTTP internal authority (the bug scenario) ─

    [Fact]
    public void AddOrkyoAuthentication_RequireHttpsMetadataFalse_WhenInternalAuthorityIsHttp()
    {
        // Production scenario: public HTTPS authority + internal HTTP Keycloak URL.
        // This was the bug: RequireHttpsMetadata was erroneously set to true based on
        // the public authority, causing the OIDC discovery fetch to fail.
        var options = BuildJwtOptions(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: "http://keycloak:8080/realms/orkyo");

        options.RequireHttpsMetadata.Should().BeFalse();
    }

    [Fact]
    public void AddOrkyoAuthentication_RequireHttpsMetadataFalse_WhenBothAuthoritiesAreHttp()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "http://localhost:8080/realms/orkyo",
            internalAuthority: "http://keycloak:8080/realms/orkyo");

        options.RequireHttpsMetadata.Should().BeFalse();
    }

    // ── RequireHttpsMetadata — with HTTPS internal authority ──────────────────

    [Fact]
    public void AddOrkyoAuthentication_RequireHttpsMetadataTrue_WhenInternalAuthorityIsHttps()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: "https://keycloak.internal/realms/orkyo");

        options.RequireHttpsMetadata.Should().BeTrue();
    }

    // ── MetadataAddress points to internal authority ──────────────────────────

    [Fact]
    public void AddOrkyoAuthentication_MetadataAddressPointsToInternalAuthority_WhenSet()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: "http://keycloak:8080/realms/orkyo");

        options.MetadataAddress.Should().Be(
            "http://keycloak:8080/realms/orkyo/.well-known/openid-configuration");
    }

    [Fact]
    public void AddOrkyoAuthentication_MetadataAddressIsNull_WhenNoInternalAuthority()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: null);

        // JwtBearerOptions auto-derives MetadataAddress from Authority when not explicitly set.
        // The invariant is that it points to the public authority, not an internal URL.
        options.MetadataAddress.Should().Be(
            "https://auth.example.com/realms/orkyo/.well-known/openid-configuration");
    }

    // ── Authority and issuer validation ───────────────────────────────────────

    [Fact]
    public void AddOrkyoAuthentication_AuthorityIsPublicUrl_EvenWhenInternalAuthoritySet()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: "http://keycloak:8080/realms/orkyo");

        options.Authority.Should().Be("https://auth.example.com/realms/orkyo");
    }

    [Fact]
    public void AddOrkyoAuthentication_ValidIssuerIsPublicAuthority()
    {
        var options = BuildJwtOptions(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: "http://keycloak:8080/realms/orkyo");

        options.TokenValidationParameters.ValidIssuer.Should().Be("https://auth.example.com/realms/orkyo");
    }

    // ── Token boundary ────────────────────────────────────────────────────────
    // The audience says who may consume a token; azp says who it was issued to; typ that it
    // is an access token. Any realm client's token carries aud=account, so that audience is
    // not accepted, and a token issued to another client (Grafana) or an ID token is refused
    // even when the audience matches.

    [Fact]
    public void AddOrkyoAuthentication_AcceptsOnlyTheBackendClientAudience()
    {
        var options = BuildJwtOptions(oidcAuthority: "https://auth.example.com/realms/orkyo", internalAuthority: null);

        options.TokenValidationParameters.ValidAudience.Should().Be("orkyo-backend");
        options.TokenValidationParameters.ValidAudiences.Should().BeNullOrEmpty("\"account\" is every realm client's audience");
    }

    [Theory]
    [InlineData("Bearer", "orkyo-backend", true)]
    [InlineData("Bearer", "orkyo-grafana", false)]
    [InlineData("ID", "orkyo-backend", false)]
    [InlineData(null, "orkyo-backend", false)]
    [InlineData("Bearer", null, false)]
    public async Task OnTokenValidated_AcceptsOnlyBearerTokensIssuedToTheBackendClient(string? typ, string? azp, bool accepted)
    {
        var options = BuildJwtOptions(oidcAuthority: "https://auth.example.com/realms/orkyo", internalAuthority: null);
        var claims = new List<Claim> { new("sub", "user-1") };
        if (typ is not null) claims.Add(new("typ", typ));
        if (azp is not null) claims.Add(new("azp", azp));
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(httpContext, scheme, options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };

        await options.Events!.OnTokenValidated(context);

        (context.Result?.Failure is null).Should().Be(accepted);
    }

    [Theory]
    [InlineData("client_id", "orkyo-backend")]
    [InlineData("clientId", "orkyo-backend")]
    [InlineData("preferred_username", "service-account-orkyo-backend")]
    public async Task OnTokenValidated_RejectsServiceAccountTokens(string claim, string value)
    {
        var context = TokenValidatedContextWith(new Claim(claim, value));

        await context.Options.Events!.OnTokenValidated(context);

        context.Result?.Failure.Should().NotBeNull("a client-credentials token names no user");
    }

    [Fact]
    public async Task OnTokenValidated_AcceptsAUserTokenWithAUsername()
    {
        var context = TokenValidatedContextWith(new Claim("preferred_username", "alice@example.com"));

        await context.Options.Events!.OnTokenValidated(context);

        context.Result?.Failure.Should().BeNull();
    }

    private static TokenValidatedContext TokenValidatedContextWith(Claim extra)
    {
        var options = BuildJwtOptions(oidcAuthority: "https://auth.example.com/realms/orkyo", internalAuthority: null);
        var claims = new List<Claim> { new("sub", "user-1"), new("typ", "Bearer"), new("azp", "orkyo-backend"), extra };
        var httpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider() };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, options)
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
        };
    }

    // ── Idempotency ───────────────────────────────────────────────────────────
    // Products call AddOrkyoAuthentication explicitly in Program.cs (per their
    // explicit-registration rule) while AddFoundationServices also calls it. The
    // guard makes the second call a no-op instead of re-adding the Bearer scheme.

    [Fact]
    public void AddOrkyoAuthentication_SecondCallIsNoOp()
    {
        var config = BuildConfig(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);

        services.AddOrkyoAuthentication(config);
        var countAfterFirstCall = services.Count;
        services.AddOrkyoAuthentication(config);

        services.Count.Should().Be(countAfterFirstCall);
    }

    [Fact]
    public async Task AddOrkyoAuthentication_CalledTwice_BearerSchemeStillResolves()
    {
        // Without the guard the second call re-adds the Bearer scheme and the
        // scheme provider throws "Scheme already exists: Bearer" at resolution.
        var config = BuildConfig(
            oidcAuthority: "https://auth.example.com/realms/orkyo",
            internalAuthority: null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddOrkyoAuthentication(config);
        services.AddOrkyoAuthentication(config);

        var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);

        scheme.Should().NotBeNull();
    }

    // ── BFF cookie name — which cookie the dual-mode selector looks for ───────
    //
    // The selector and the BFF handler must agree on the name. They did not: an unset
    // BFF_COOKIE_NAME is written into the deployed .env as an EMPTY value, and `?? Default`
    // treats empty as set — so the selector looked for a cookie named "" (never present)
    // while the handler used the real default, and every cookie session fell through to
    // bearer. Empty must resolve to the default, exactly as a missing key does.

    [Theory]
    [InlineData(null)]  // key absent
    [InlineData("")]    // key present but empty — the deployed shape
    public void ForwardDefaultSelector_WithNoConfiguredCookieName_MatchesTheDefaultCookie(
        string? configured)
    {
        var selector = BuildForwardSelector(configured);

        selector(ContextWithCookie(BffOptions.DefaultCookieName))
            .Should().Be(BffCookieAuthenticationHandler.SchemeName);
    }

    [Fact]
    public void ForwardDefaultSelector_WithAConfiguredCookieName_MatchesThatCookie()
    {
        var selector = BuildForwardSelector("orkyo-custom-session");

        selector(ContextWithCookie("orkyo-custom-session"))
            .Should().Be(BffCookieAuthenticationHandler.SchemeName);
        // The default name is no longer what the selector is looking for.
        selector(ContextWithCookie(BffOptions.DefaultCookieName))
            .Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void ForwardDefaultSelector_PrefersTheAuthorizationHeaderOverAnyCookie()
    {
        var selector = BuildForwardSelector(configuredCookieName: null);
        var context = ContextWithCookie(BffOptions.DefaultCookieName);
        context.Request.Headers.Authorization = "Bearer a-token";

        selector(context).Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }

    private static HttpContext ContextWithCookie(string cookieName)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{cookieName}=value";
        return context;
    }

    private static Func<HttpContext, string?> BuildForwardSelector(string? configuredCookieName)
    {
        var values = new Dictionary<string, string?>
        {
            [ConfigKeys.OidcAuthority] = "https://auth.example.com/realms/orkyo",
            [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
            [ConfigKeys.BffEnabled] = "true",
        };
        if (configuredCookieName != null)
            values[ConfigKeys.BffCookieName] = configuredCookieName;

        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddOrkyoAuthentication(config);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<PolicySchemeOptions>>()
            .Get("OrkYoPolicy");

        options.ForwardDefaultSelector.Should().NotBeNull();
        return options.ForwardDefaultSelector!;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildConfig(string oidcAuthority, string? internalAuthority)
    {
        var values = new Dictionary<string, string?>
        {
            [ConfigKeys.OidcAuthority] = oidcAuthority,
            [ConfigKeys.KeycloakBackendClientId] = "orkyo-backend",
        };
        if (internalAuthority != null)
            values[ConfigKeys.OidcInternalAuthority] = internalAuthority;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static JwtBearerOptions BuildJwtOptions(string oidcAuthority, string? internalAuthority)
    {
        var config = BuildConfig(oidcAuthority, internalAuthority);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddOrkyoAuthentication(config);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
