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

    /// <summary>The configuration every product host receives; see <see cref="TestConfiguration.Shared"/>.</summary>
    public static IReadOnlyDictionary<string, string?> SharedConfiguration => TestConfiguration.Shared;

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

    /// <summary>
    /// Product configuration sources, added after the shared in-memory collection. A later source
    /// wins, so this is the hook for a value that must beat the in-memory block — an environment
    /// variable provider, or a second collection that removes a key the shared block sets.
    /// </summary>
    protected virtual void ConfigureAppConfigurationSources(IConfigurationBuilder config)
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

            ConfigureAppConfigurationSources(config);
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
