using Api.Endpoints;
using Api.Security;
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
            options.RequireHttpsMetadata = !oidcAuthority.StartsWith("http://");
            // Disable claim type remapping so JWT claim names (sub, email, etc.)
            // are preserved as-is. Without this, ASP.NET Core maps "sub" to
            // ClaimTypes.NameIdentifier, breaking FindFirst("sub").
            options.MapInboundClaims = false;

            if (!string.IsNullOrEmpty(metadataAddress))
            {
                options.MetadataAddress = metadataAddress;
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
                NameClaimType = "preferred_username",
                RoleClaimType = "realm_access.roles",
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    // Opaque bearer tokens (e.g. METRICS_TOKEN on /metrics) are not JWTs.
                    // The endpoint filter handles those separately — suppress the noise here.
                    if (context.Exception is SecurityTokenMalformedException)
                        return Task.CompletedTask;

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
                    var sub = context.Principal?.FindFirst("sub")?.Value ?? "(none)";
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
                        if (context.Request.Headers.ContainsKey("Authorization"))
                            return JwtBearerDefaults.AuthenticationScheme;

                        // BFF session cookie present → use BFF cookie handler
                        if (context.Request.Cookies.ContainsKey(bffCookieName))
                            return BffCookieAuthenticationHandler.SchemeName;

                        // Default to JWT Bearer (will fail with 401 if no token)
                        return JwtBearerDefaults.AuthenticationScheme;
                    };
                });
        }

        services.AddAuthorization();
        return services;
    }
}
