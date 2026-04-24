using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

namespace Api.Configuration;

/// <summary>
/// Immutable snapshot of deployment-time configuration sourced from environment
/// variables at startup.  Registered as a singleton — values never change while
/// the process is running.
///
/// Secrets are masked by <see cref="Redacted"/> for diagnostic output.
/// </summary>
public sealed record DeploymentConfig
{
    // ── URLs ─────────────────────────────────────────────────────────────
    public required string PublicUrl { get; init; }
    public required string AuthPublicUrl { get; init; }
    public required string AppBaseUrl { get; init; }
    public required string CorsAllowedOrigins { get; init; }

    // ── SMTP ─────────────────────────────────────────────────────────────
    public required string SmtpHost { get; init; }
    public required int SmtpPort { get; init; }
    public required bool SmtpUseSsl { get; init; }
    public required string SmtpFromEmail { get; init; }
    public required string SmtpFromName { get; init; }
    public string? SmtpUsername { get; init; }
    public string? SmtpPassword { get; init; }

    // ── Storage ──────────────────────────────────────────────────────────
    public required string FileStoragePath { get; init; }

    // ── Identity ─────────────────────────────────────────────────────────
    public required string OidcAuthority { get; init; }
    public required string KeycloakUrl { get; init; }
    public required string KeycloakRealm { get; init; }
    public required string KeycloakBackendClientId { get; init; }
    public required string KeycloakBackendClientSecret { get; init; }

    // ── Database ─────────────────────────────────────────────────────────
    public required string PostgresConnectionString { get; init; }

    // ── Logging ──────────────────────────────────────────────────────────
    public string LogLevel { get; init; } = "Information";

    // ── Version ──────────────────────────────────────────────────────────
    public string? Version { get; init; }

    // ── Required keys ────────────────────────────────────────────────────

    /// <summary>
    /// All configuration keys that must be set for the application to start.
    /// Single source of truth — used by <see cref="ConfigurationValidator"/>
    /// and the <c>--validate</c> CLI mode.
    /// </summary>
    public static IReadOnlyList<string> RequiredKeys { get; } =
    [
        // Auth — Keycloak OIDC is the only supported mode
        ConfigKeys.OidcAuthority,
        ConfigKeys.KeycloakUrl,
        ConfigKeys.KeycloakRealm,
        ConfigKeys.KeycloakBackendClientId,
        ConfigKeys.KeycloakBackendClientSecret,

        // Database
        "ConnectionStrings:Postgres",

        // URLs
        ConfigKeys.AppBaseUrl,

        // File storage
        ConfigKeys.FileStoragePath,

        // Email
        ConfigKeys.SmtpHost,
        ConfigKeys.SmtpPort,
        ConfigKeys.SmtpUseSsl,
        ConfigKeys.SmtpFromEmail,
        ConfigKeys.SmtpFromName,
    ];

    // ── Factory ──────────────────────────────────────────────────────────

    /// <summary>
    /// Build a <see cref="DeploymentConfig"/> from the application's
    /// <see cref="IConfiguration"/>.  Throws on missing required values
    /// (note: <see cref="ConfigurationValidator"/> already runs first).
    /// </summary>
    public static DeploymentConfig FromConfiguration(IConfiguration configuration)
    {
        string Require(string key) =>
            configuration[key]
            ?? throw new InvalidOperationException($"DeploymentConfig: required key '{key}' is not set");

        return new DeploymentConfig
        {
            PublicUrl = Require(ConfigKeys.AppBaseUrl),
            AuthPublicUrl = configuration[ConfigKeys.OidcAuthority] ?? Require(ConfigKeys.KeycloakUrl),
            AppBaseUrl = Require(ConfigKeys.AppBaseUrl),
            CorsAllowedOrigins = configuration[ConfigKeys.CorsAllowedOrigins] ?? "",

            SmtpHost = Require(ConfigKeys.SmtpHost),
            SmtpPort = int.Parse(Require(ConfigKeys.SmtpPort)),
            SmtpUseSsl = bool.Parse(Require(ConfigKeys.SmtpUseSsl)),
            SmtpFromEmail = Require(ConfigKeys.SmtpFromEmail),
            SmtpFromName = Require(ConfigKeys.SmtpFromName),
            SmtpUsername = configuration[ConfigKeys.SmtpUsername],
            SmtpPassword = configuration[ConfigKeys.SmtpPassword],

            FileStoragePath = configuration[ConfigKeys.FileStoragePath] ?? "/app/storage",
            OidcAuthority = Require(ConfigKeys.OidcAuthority),
            KeycloakUrl = Require(ConfigKeys.KeycloakUrl),
            KeycloakRealm = Require(ConfigKeys.KeycloakRealm),
            KeycloakBackendClientId = Require(ConfigKeys.KeycloakBackendClientId),
            KeycloakBackendClientSecret = Require(ConfigKeys.KeycloakBackendClientSecret),

            PostgresConnectionString = Require("ConnectionStrings:Postgres"),

            LogLevel = configuration["Logging:LogLevel:Default"] ?? "Information",
            Version = configuration["ORKYO_VERSION"],
        };
    }

    // ── Redaction ────────────────────────────────────────────────────────

    private static readonly HashSet<string> SecretProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SmtpPassword),
        nameof(SmtpUsername),
        nameof(KeycloakBackendClientSecret),
        nameof(PostgresConnectionString),
    };

    /// <summary>
    /// Returns a dictionary of all properties with secrets masked as "***".
    /// Safe for logging, admin UI display, and diagnostic endpoints.
    /// </summary>
    public Dictionary<string, string?> Redacted()
    {
        var result = new Dictionary<string, string?>();
        foreach (var prop in typeof(DeploymentConfig).GetProperties())
        {
            var value = prop.GetValue(this);
            if (SecretProperties.Contains(prop.Name) && value != null)
            {
                result[prop.Name] = "***";
            }
            else
            {
                result[prop.Name] = value?.ToString();
            }
        }
        return result;
    }
}
