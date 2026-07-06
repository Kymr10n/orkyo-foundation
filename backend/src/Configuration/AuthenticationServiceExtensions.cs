using Api.Endpoints;
using Api.Constants;
using Api.Integrations.Keycloak;
using Api.Reporting.Auth;
using Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Orkyo.Shared;

namespace Api.Configuration;

/// <summary>
/// Registers JWT authentication using Keycloak OIDC.
///
/// The API validates tokens issued by the Keycloak realm using the
/// authority's JWKS endpoint (no shared secret needed).
///
/// OIDC_AUTHORITY must be set — there is no fallback authentication mode.
/// The internal authority (OIDC_INTERNAL_AUTHORITY) is optional; use it
/// when the Docker-internal hostname differs from the browser-facing URL.
/// </summary>
public static class AuthenticationServiceExtensions
{
    public static IServiceCollection AddOrkyoAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Idempotent: products call this explicitly in Program.cs (per their
        // explicit-registration rule) while AddFoundationServices also calls it.
        // Without the guard the second call would re-add the Bearer scheme and
        // throw "Scheme already exists" at startup.
        if (services.Any(d => d.ServiceType == typeof(OrkyoAuthenticationMarker)))
            return services;
        services.AddSingleton<OrkyoAuthenticationMarker>();

        var oidcAuthority = configuration.GetRequired(ConfigKeys.OidcAuthority);
        // The BFF uses the confidential orkyo-backend client for OIDC — tokens carry its audience
        var oidcClientId = configuration.GetRequired(ConfigKeys.KeycloakBackendClientId);

        var internalAuthority = configuration[ConfigKeys.OidcInternalAuthority];
        var metadataAddress = string.IsNullOrEmpty(internalAuthority)
            ? null
            : $"{internalAuthority.TrimEnd('/')}/.well-known/openid-configuration";

        var bffEnabled = string.Equals(configuration[ConfigKeys.BffEnabled], "true", StringComparison.OrdinalIgnoreCase);
        var bffCookieName = configuration[ConfigKeys.BffCookieName] ?? BffOptions.DefaultCookieName;

        services.AddAuthentication(options =>
        {
            if (bffEnabled)
            {
                // Dual-mode: route to JWT or BFF based on request content
                options.DefaultAuthenticateScheme = "OrkYoPolicy";
                options.DefaultChallengeScheme = "OrkYoPolicy";
            }
            else
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }
        })
        .AddJwtBearer(options =>
        {
            options.Authority = oidcAuthority;
            // Disable claim type remapping so JWT claim names (sub, email, etc.)
            // are preserved as-is. Without this, ASP.NET Core maps "sub" to
            // ClaimTypes.NameIdentifier, breaking FindFirst(KeycloakClaims.Subject).
            options.MapInboundClaims = false;

            if (!string.IsNullOrEmpty(metadataAddress))
            {
                options.MetadataAddress = metadataAddress;
                // RequireHttpsMetadata must reflect the URL actually fetched for OIDC discovery,
                // not the public authority. Internal container-to-container traffic uses HTTP
                // while the public authority is HTTPS — these are independent concerns.
                options.RequireHttpsMetadata = metadataAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                options.RequireHttpsMetadata = oidcAuthority.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            }

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = oidcAuthority,
                // Validate audience against the client ID and the standard "account" service
                ValidateAudience = true,
                ValidAudiences = [oidcClientId, "account"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                NameClaimType = KeycloakClaims.PreferredUsername,
                RoleClaimType = KeycloakClaims.RealmRolesClaim,
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<EndpointLoggerCategory>>();
                    logger.LogWarning("JWT authentication failed: {Error}",
                        context.Exception.Message);
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<EndpointLoggerCategory>>();
                    var sub = context.Principal?.FindFirst(KeycloakClaims.Subject)?.Value ?? "(none)";
                    logger.LogDebug("Token validated for subject {Sub}", sub);
                    return Task.CompletedTask;
                }
            };
        });

        if (bffEnabled)
        {
            services.AddAuthentication()
                .AddPolicyScheme("OrkYoPolicy", "OrkYo Dual-Mode Auth", options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        // Authorization header present → use JWT Bearer
                        if (context.Request.Headers.ContainsKey(HeaderConstants.Authorization))
                            return JwtBearerDefaults.AuthenticationScheme;

                        // BFF session cookie present → use BFF cookie handler
                        if (context.Request.Cookies.ContainsKey(bffCookieName))
                            return BffCookieAuthenticationHandler.SchemeName;

                        // Default to JWT Bearer (will fail with 401 if no token)
                        return JwtBearerDefaults.AuthenticationScheme;
                    };
                });
        }

        // Register the reporting token auth scheme as a standalone scheme.
        // Reporting endpoints opt in via .RequireAuthorization("ReportingToken").
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ReportingTokenAuthHandler>(
                ReportingTokenAuthHandler.SchemeName, _ => { });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(ReportingTokenAuthHandler.PolicyName, policy =>
            {
                policy.AddAuthenticationSchemes(ReportingTokenAuthHandler.SchemeName);
                policy.RequireAuthenticatedUser();
            });
        });

        return services;
    }

    /// <summary>Registration marker making <see cref="AddOrkyoAuthentication"/> idempotent.</summary>
    private sealed class OrkyoAuthenticationMarker;
}
