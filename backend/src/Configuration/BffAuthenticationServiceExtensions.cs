using Api.Middleware;
using Api.Security;
using Api.Services.BffSession;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkyo.Shared;
using StackExchange.Redis;

namespace Api.Configuration;

/// <summary>
/// Registers BFF cookie authentication services when <c>BFF_ENABLED=true</c>.
///
/// BffOptions binding and core services are always registered (harmless no-op
/// when BFF is disabled). The auth scheme is only added when enabled.
/// This supports test scenarios where configuration is applied after
/// service registration (e.g. WebApplicationFactory.ConfigureAppConfiguration).
/// </summary>
public static class BffAuthenticationServiceExtensions
{
    /// <summary>The one reading of <c>BFF_ENABLED</c> (registration, the scheme selector, the endpoints).</summary>
    internal static bool IsBffEnabled(IConfiguration configuration) =>
        string.Equals(configuration[ConfigKeys.BffEnabled], "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one reading of <c>BFF_COOKIE_NAME</c>. Empty counts as absent: the deploy pipeline
    /// writes <c>KEY=</c> for an unset key, and a <c>??</c> fallback would then look for a cookie
    /// named "" (the documented cookie-name bug).
    /// </summary>
    internal static string CookieName(IConfiguration configuration) =>
        string.IsNullOrEmpty(configuration[ConfigKeys.BffCookieName])
            ? BffOptions.DefaultCookieName
            : configuration[ConfigKeys.BffCookieName]!;

    public static IServiceCollection AddBffAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Always bind BffOptions from configuration (deferred — reads config at resolution time)
        services.AddOptions<BffOptions>()
            .Configure<IConfiguration, IHostEnvironment>((opts, config, env) =>
            {
                opts.CookieName = CookieName(config);

                // Treat empty string as null — omit Domain attribute so the cookie
                // defaults to the exact request host (required for localhost dev).
                var domain = config[ConfigKeys.BffCookieDomain];
                opts.CookieDomain = string.IsNullOrWhiteSpace(domain) ? null : domain;

                // Default Secure=true in production, false in Development
                var secureSetting = config[ConfigKeys.BffCookieSecure];
                if (!string.IsNullOrEmpty(secureSetting))
                    opts.CookieSecure = !string.Equals(secureSetting, "false", StringComparison.OrdinalIgnoreCase);
                else
                    opts.CookieSecure = !env.IsDevelopment();

                opts.RedirectUri = config.GetOptionalString(ConfigKeys.BffRedirectUri);

                // Canonical public app origin (carries the port) — the preferred base for
                // default/error redirects so they don't fall back to the port-less host list.
                opts.AppBaseUrl = config.GetOptionalString(ConfigKeys.AppBaseUrl);

                var allowedHosts = config[ConfigKeys.BffAllowedHosts];
                if (!string.IsNullOrEmpty(allowedHosts))
                    opts.AllowedReturnToHosts = allowedHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // A set-but-unparseable duration refuses startup rather than silently
                // keeping the compiled-in default: a typo in prod would otherwise change
                // session length with nothing in any log to say so.
                opts.SessionIdleDuration = ParseDurationOrThrow(
                    config[ConfigKeys.BffSessionIdleDuration], ConfigKeys.BffSessionIdleDuration, opts.SessionIdleDuration);
                opts.SessionMaxDuration = ParseDurationOrThrow(
                    config[ConfigKeys.BffSessionMaxDuration], ConfigKeys.BffSessionMaxDuration, opts.SessionMaxDuration);

                var scopes = config[ConfigKeys.BffScopes];
                if (!string.IsNullOrEmpty(scopes))
                    opts.Scopes = scopes;
            });

        // Register TenantMiddlewareOptions via the options pattern so it can be
        // injected as IOptions<TenantMiddlewareOptions> rather than manually bound
        // from IConfiguration inside individual handlers.
        services.Configure<TenantMiddlewareOptions>(configuration.GetSection(ConfigKeys.TenantResolutionSection));

        // Register PKCE state store — Valkey (atomic GETDEL) in production,
        // in-memory (ConcurrentDictionary.TryRemove) in development / test.
        var valkeyConnection = configuration[ConfigKeys.ValkeyConnection];
        if (!string.IsNullOrEmpty(valkeyConnection))
        {
            services.AddSingleton<IBffSessionStore, ValkeyBffSessionStore>();
            services.AddSingleton<IBffPkceStateStore, ValkeyBffPkceStateStore>();
        }
        else
        {
            services.AddSingleton<IBffSessionStore, InMemoryBffSessionStore>();
            services.AddSingleton<IBffPkceStateStore, InMemoryBffPkceStateStore>();
        }

        // Data Protection for encrypting session cookie values.
        // Persist keys to Valkey when Valkey is configured so they survive container
        // restarts and are shared across blue/green deployment slots. SetApplicationName
        // ensures keys are scoped to this app regardless of the host process name.
        // The keys go through the process's one IConnectionMultiplexer (AddOrkyoValkey), resolved
        // when Data Protection first needs them — not a second connection opened here, eagerly,
        // at registration.
        services.AddDataProtection()
            .SetApplicationName("orkyo");
        if (!string.IsNullOrEmpty(valkeyConnection))
            services.AddOptions<KeyManagementOptions>()
                .Configure<IConnectionMultiplexer>((options, valkey) =>
                    options.XmlRepository = new RedisXmlRepository(() => valkey.GetDatabase(), "DataProtection-Keys"));

        // Keycloak token endpoint: login code exchange and session refresh.
        services.AddHttpClient(KeycloakTokenClient.HttpClientName);
        services.AddSingleton<KeycloakTokenClient>();

        // Only register the BFF cookie auth scheme when enabled
        if (IsBffEnabled(configuration))
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, BffCookieAuthenticationHandler>(
                    BffCookieAuthenticationHandler.SchemeName, _ => { });
        }

        return services;
    }

    /// <summary>Unset keeps the default; set-but-invalid is a startup error, never a silent fallback.</summary>
    private static TimeSpan ParseDurationOrThrow(string? raw, string key, TimeSpan defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (TimeSpan.TryParse(raw, out var parsed)) return parsed;
        throw new InvalidOperationException(
            $"{key} is set to '{raw}', which is not a valid TimeSpan (use e.g. '00:45:00').");
    }

}
