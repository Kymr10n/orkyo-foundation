using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// Base for a product's <c>WebApplicationFactory&lt;Program&gt;</c>: boots the real composition
/// root under the <c>Test</c> environment with the shared in-memory configuration block, then
/// layers the test authentication scheme and the Keycloak admin mock over it. A product adds its
/// own configuration keys (connection strings, Valkey stance) and service overrides (an in-memory
/// rate-limit store, a health-check re-registration) through the two hooks.
/// </summary>
/// <typeparam name="TProgram">The product's <c>Program</c> entry-point type.</typeparam>
public abstract class ProductWebApplicationFactoryBase<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    /// <summary>Shared Keycloak mock — tests can inspect calls and configure responses.</summary>
    public MockKeycloakAdminService MockKeycloakAdminService { get; } = new();

    /// <summary>
    /// The configuration every product host receives: SMTP and app URLs, unreachable Keycloak
    /// hosts, BFF enabled with test-friendly settings, and the deterministic master key.
    /// </summary>
    /// <remarks>
    /// The Keycloak hosts use <c>.invalid</c> (RFC 2606), which fails DNS resolution instantly.
    /// A <c>.local</c> host triggers mDNS/Avahi resolution that hangs ~15s before failing, adding
    /// ~20s to every test that exercises an (intentionally unreachable) Keycloak.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> SharedConfiguration { get; } = new Dictionary<string, string?>
    {
        ["ASPNETCORE_ENVIRONMENT"] = TestConstants.EnvironmentName,
        ["SMTP_HOST"] = "localhost",
        ["SMTP_PORT"] = "1025",
        ["SMTP_USE_SSL"] = "false",
        ["SMTP_FROM_EMAIL"] = "test@test.local",
        ["SMTP_FROM_NAME"] = "Test",
        ["APP_BASE_URL"] = "http://localhost:5173",
        ["CORS_ALLOWED_ORIGINS"] = "http://localhost:5173",
        ["OIDC_AUTHORITY"] = "http://test-keycloak.invalid/realms/test",
        ["KEYCLOAK_URL"] = "http://test-keycloak.invalid",
        ["KEYCLOAK_REALM"] = "test",
        ["KEYCLOAK_BACKEND_CLIENT_ID"] = "test-backend",
        ["KEYCLOAK_BACKEND_CLIENT_SECRET"] = "test-backend-secret",
        ["BFF_ENABLED"] = "true",
        ["BFF_REDIRECT_URI"] = "http://localhost/api/auth/bff/callback",
        ["BFF_ALLOWED_HOSTS"] = "orkyo.com,*.orkyo.com,localhost",
        ["BFF_COOKIE_SECURE"] = "false",
        // Satisfies the configuration validator's format check (valid base64, exactly 32 bytes).
        // No real encryption runs in integration tests so the value is never used operationally.
        ["ORKYO_MASTER_ENCRYPTION_KEY"] = TestConstants.MasterEncryptionKey,
    };

    /// <summary>
    /// Adds the product's configuration on top of <see cref="SharedConfiguration"/>: the
    /// connection strings for its database topology and anything its <c>Program.cs</c> requires.
    /// A key set here overrides the shared value. Set a key to null to force a lower-priority
    /// provider's value out of the way only if that provider does not define it (null values do
    /// not override <c>appsettings.Test.json</c>).
    /// </summary>
    protected abstract void AddProductConfiguration(IDictionary<string, string?> configuration);

    /// <summary>
    /// Product service overrides, applied after the test scheme and the Keycloak mock are in
    /// place — the last registration wins, so <c>RemoveAll&lt;T&gt;()</c> + <c>Add</c> here
    /// replaces anything <c>Program.cs</c> registered (an in-memory rate-limit store, a
    /// break-glass store, the Postgres health check pointed at the test port).
    /// </summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(TestConstants.EnvironmentName);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.Sources.Clear();
            config.AddJsonFile("appsettings.json", optional: false);
            config.AddJsonFile("appsettings.Test.json", optional: true);

            var values = new Dictionary<string, string?>(SharedConfiguration);
            AddProductConfiguration(values);
            config.AddInMemoryCollection(values);
        });

        builder.ConfigureServices(services =>
        {
            services.Configure<ServiceProviderOptions>(o =>
            {
                o.ValidateOnBuild = true;
                o.ValidateScopes = true;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestConstants.AuthScheme;
                options.DefaultChallengeScheme = TestConstants.AuthScheme;
                options.DefaultScheme = TestConstants.AuthScheme;
            })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestConstants.AuthScheme, _ => { });

            services.RemoveAll<IKeycloakAdminService>();
            services.AddSingleton<IKeycloakAdminService>(MockKeycloakAdminService);

            ConfigureTestServices(services);
        });
    }
}
