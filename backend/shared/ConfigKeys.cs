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
    public const string ConnectionStringPostgres = "Postgres";
    public const string ConnectionStringControlPlane = "ControlPlane";
    public const string ConnectionStringRedis = "ConnectionStrings:Redis";
    public const string RedisConnection = "REDIS_CONNECTION";

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
    public const string FileStoragePath = "FILE_STORAGE_PATH";
    public const string DefaultTenantDb = "DEFAULT_TENANT_DB";
    public const string InitialAdminEmail = "INITIAL_ADMIN_EMAIL";

    // ── Tenant resolution ───────────────────────────────────────────────────
    public const string TenantResolutionBaseDomain = "TenantResolution:BaseDomain";
    public const string TenantResolutionAllowTenantHeader = "TenantResolution:AllowTenantHeader";

    // ── Terms of Service ────────────────────────────────────────────────────
    public const string TosRequiredVersion = "ToS:RequiredVersion";

    // ── Security / Challenge ────────────────────────────────────────────────
    public const string TurnstileSecretKey = "TURNSTILE_SECRET_KEY";

    // ── Observability ───────────────────────────────────────────────────────
    public const string OtelExporterOtlpEndpoint = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string LokiUrl = "LOKI_URL";
    public const string MetricsToken = "METRICS_TOKEN";

    // ── Notifications ───────────────────────────────────────────────────────
    public const string AlertEmailTo = "ALERT_EMAIL_TO";
    public const string ContactNotificationEmail = "CONTACT_NOTIFICATION_EMAIL";
}
