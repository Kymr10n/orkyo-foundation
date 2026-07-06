using Api.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
