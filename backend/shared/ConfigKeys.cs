namespace Orkyo.Shared;

/// <summary>
/// Configuration key constants for all environment variables and config paths.
/// Single source of truth — used by API, Worker, and tests.
/// </summary>
public static class ConfigKeys
{
    // ── Environment ─────────────────────────────────────────────────────────
    public const string AspNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";

    // ── Connection strings ──────────────────────────────────────────────────
    // Three shapes per connection, by consumer:
    //  - short name           → configuration.GetConnectionString(...)
    //  - "ConnectionStrings:" → configuration[...] indexer / Require(...)
    //  - "ConnectionStrings__"→ Environment.GetEnvironmentVariable(...) (ASP.NET env-var convention)
    public const string ConnectionStringPostgres = "Postgres";
    public const string ConnectionStringControlPlane = "ControlPlane";
    public const string ConnectionStringPostgresPath = "ConnectionStrings:Postgres";
    public const string ConnectionStringControlPlanePath = "ConnectionStrings:ControlPlane";
    public const string ConnectionStringPostgresEnvVar = "ConnectionStrings__Postgres";
    public const string ConnectionStringControlPlaneEnvVar = "ConnectionStrings__ControlPlane";
    /// <summary>Legacy alias for <see cref="ConnectionStringControlPlaneEnvVar"/> still honored by the migrator.</summary>
    public const string ControlPlaneConnectionLegacyEnvVar = "CONTROL_PLANE_CONNECTION_STRING";
    public const string ConnectionStringValkey = "ConnectionStrings:Valkey";
    public const string ValkeyConnection = "VALKEY_CONNECTION";

    // ── OIDC / Keycloak ─────────────────────────────────────────────────────
    public const string OidcAuthority = "OIDC_AUTHORITY";
    public const string OidcInternalAuthority = "OIDC_INTERNAL_AUTHORITY";
    public const string KeycloakUrl = "KEYCLOAK_URL";
    public const string KeycloakRealm = "KEYCLOAK_REALM";
    public const string KeycloakBackendClientId = "KEYCLOAK_BACKEND_CLIENT_ID";
    public const string KeycloakBackendClientSecret = "KEYCLOAK_BACKEND_CLIENT_SECRET";
    public const string KeycloakInternalUrl = "KEYCLOAK_INTERNAL_URL";

    // ── BFF (Backend-For-Frontend) ──────────────────────────────────────────
    public const string BffEnabled = "BFF_ENABLED";
    public const string BffCookieName = "BFF_COOKIE_NAME";
    public const string BffCookieDomain = "BFF_COOKIE_DOMAIN";
    public const string BffCookieSecure = "BFF_COOKIE_SECURE";
    public const string BffRedirectUri = "BFF_REDIRECT_URI";
    public const string BffAllowedHosts = "BFF_ALLOWED_HOSTS";
    public const string BffSessionDuration = "BFF_SESSION_DURATION";
    public const string BffScopes = "BFF_SCOPES";

    // ── SMTP ────────────────────────────────────────────────────────────────
    public const string SmtpHost = "SMTP_HOST";
    public const string SmtpPort = "SMTP_PORT";
    public const string SmtpUseSsl = "SMTP_USE_SSL";
    public const string SmtpUsername = "SMTP_USERNAME";
    public const string SmtpPassword = "SMTP_PASSWORD";
    public const string SmtpFromEmail = "SMTP_FROM_EMAIL";
    public const string SmtpFromName = "SMTP_FROM_NAME";

    // ── Application ─────────────────────────────────────────────────────────
    public const string AppBaseUrl = "APP_BASE_URL";
    public const string CorsAllowedOrigins = "CORS_ALLOWED_ORIGINS";
    public const string InitialAdminEmail = "INITIAL_ADMIN_EMAIL";

    // ── Tenant resolution ───────────────────────────────────────────────────
    public const string TenantResolutionSection = "TenantResolution";
    public const string TenantResolutionBaseDomain = "TenantResolution:BaseDomain";
    public const string TenantResolutionSubdomainPrefix = "TenantResolution:SubdomainPrefix";
    public const string TenantResolutionAllowTenantHeader = "TenantResolution:AllowTenantHeader";

    // ── Terms of Service ────────────────────────────────────────────────────
    public const string TosRequiredVersion = "ToS:RequiredVersion";

    // ── Security / Challenge ────────────────────────────────────────────────
    public const string TurnstileSecretKey = "TURNSTILE_SECRET_KEY";

    /// <summary>
    /// Optional comma-separated CIDR list of trusted reverse-proxy networks whose
    /// forwarded-IP headers (CF-Connecting-IP / X-Forwarded-For) may be believed.
    /// When unset, only private (RFC1918) and loopback peers are trusted — which is
    /// correct for the Docker/nginx topology where the backend is never exposed
    /// publicly. Set explicitly to tighten beyond the private-range default.
    /// </summary>
    public const string SecurityTrustedProxyNetworks = "SECURITY_TRUSTED_PROXY_NETWORKS";

    /// <summary>Base64-encoded 32-byte AES-256 master key for application-level field/blob encryption.</summary>
    public const string MasterEncryptionKey = "ORKYO_MASTER_ENCRYPTION_KEY";

    /// <summary>Pepper for reporting-token hashing; falls back to <see cref="KeycloakBackendClientSecret"/>.</summary>
    public const string ReportingTokenPepper = "REPORTING_TOKEN_PEPPER";

    /// <summary>Disables the rate limiter (test/dev only). Read by both product Program.cs and test factories.</summary>
    public const string DisableRateLimiting = "DISABLE_RATE_LIMITING";

    // ── Observability ───────────────────────────────────────────────────────
    public const string OtelExporterOtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string LokiUrl = "LOKI_URL";
    public const string MetricsToken = "METRICS_TOKEN";
    public const string LoggingLevelDefault = "Logging:LogLevel:Default";

    // ── Version / build info ────────────────────────────────────────────────
    public const string OrkyoVersion = "ORKYO_VERSION";
    public const string OrkyoBuildSha = "ORKYO_BUILD_SHA";

    // ── Notifications ───────────────────────────────────────────────────────
    public const string AlertEmailTo = "ALERT_EMAIL_TO";
    public const string ContactNotificationEmail = "CONTACT_NOTIFICATION_EMAIL";
    public const string FeedbackNotificationEmail = "FEEDBACK_NOTIFICATION_EMAIL";

    // ── Reporting ───────────────────────────────────────────────────────────
    public const string ReportingPeopleLevelEnabled = "reporting.people_level_enabled";
}
