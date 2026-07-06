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

    // ── Identity ─────────────────────────────────────────────────────────
    public required string OidcAuthority { get; init; }
    public required string KeycloakUrl { get; init; }
    public required string KeycloakRealm { get; init; }
    public required string KeycloakBackendClientId { get; init; }
    public required string KeycloakBackendClientSecret { get; init; }

    // ── Database ─────────────────────────────────────────────────────────
    public required string PostgresConnectionString { get; init; }

    // ── Encryption ───────────────────────────────────────────────────────
    /// <summary>Base64-encoded 32-byte AES-256 master key for at-rest field/blob encryption.</summary>
    public required string MasterEncryptionKey { get; init; }

    // ── Logging ──────────────────────────────────────────────────────────
    public string LogLevel { get; init; } = "Information";

    // ── Internal identity ────────────────────────────────────────────────
    /// <summary>
    /// Internal OIDC authority URL used for server-to-server calls (e.g. diagnostics probe).
    /// Falls back to <see cref="OidcAuthority"/> when not set.
    /// </summary>
    public string? OidcInternalAuthority { get; init; }

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
        ConfigKeys.ConnectionStringPostgresPath,

        // Encryption
        ConfigKeys.MasterEncryptionKey,

        // URLs
        ConfigKeys.AppBaseUrl,

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

        var config = new DeploymentConfig
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

            OidcAuthority = Require(ConfigKeys.OidcAuthority),
            OidcInternalAuthority = configuration[ConfigKeys.OidcInternalAuthority],
            KeycloakUrl = Require(ConfigKeys.KeycloakUrl),
            KeycloakRealm = Require(ConfigKeys.KeycloakRealm),
            KeycloakBackendClientId = Require(ConfigKeys.KeycloakBackendClientId),
            KeycloakBackendClientSecret = Require(ConfigKeys.KeycloakBackendClientSecret),

            PostgresConnectionString = Require(ConfigKeys.ConnectionStringPostgresPath),

            MasterEncryptionKey = Require(ConfigKeys.MasterEncryptionKey),

            LogLevel = configuration[ConfigKeys.LoggingLevelDefault] ?? "Information",
            Version = configuration[ConfigKeys.OrkyoVersion],
        };

        // Fail fast at startup on a malformed master key (invalid base64 / not 32 bytes) — matches the
        // intent documented on DecodeMasterEncryptionKey. FromConfiguration is called eagerly in each
        // edition's Program.cs, so a bad key now crashes the container at boot instead of surfacing
        // later as a runtime 500 on the first encrypt/asset request.
        config.DecodeMasterEncryptionKey();

        return config;
    }

    // ── Redaction ────────────────────────────────────────────────────────

    private static readonly HashSet<string> SecretProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(SmtpPassword),
        nameof(SmtpUsername),
        nameof(KeycloakBackendClientSecret),
        nameof(PostgresConnectionString),
        nameof(MasterEncryptionKey),
    };

    /// <summary>
    /// Decodes and validates the master encryption key (base64 → 32 bytes).
    /// Throws <see cref="InvalidOperationException"/> on a malformed or wrong-length key
    /// so a misconfigured deployment fails fast at startup rather than at first encrypt.
    /// </summary>
    public byte[] DecodeMasterEncryptionKey()
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(MasterEncryptionKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"{ConfigKeys.MasterEncryptionKey} is not valid base64.", ex);
        }
        if (key.Length != 32)
            throw new InvalidOperationException(
                $"{ConfigKeys.MasterEncryptionKey} must decode to exactly 32 bytes (got {key.Length}).");
        return key;
    }

    /// <summary>
    /// The record-generated ToString would print every property, secrets included —
    /// one careless <c>logger.LogInformation("{Config}", config)</c> would dump the
    /// Keycloak client secret, SMTP password, master key, and connection string to the
    /// log sink. Render the redacted view instead so the object is safe to log wholesale.
    /// </summary>
    public override string ToString() =>
        $"DeploymentConfig {{ {string.Join(", ", Redacted().Select(kv => $"{kv.Key} = {kv.Value}"))} }}";

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
