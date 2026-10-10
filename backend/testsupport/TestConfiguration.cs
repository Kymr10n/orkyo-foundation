namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// The in-memory configuration block shared by every test host: each product's
/// <see cref="ProductWebApplicationFactoryBase{TProgram}"/> and foundation's own test host.
/// </summary>
public static class TestConfiguration
{
    /// <summary>
    /// The configuration every test host receives: SMTP and app URLs, unreachable Keycloak
    /// hosts, BFF enabled with test-friendly settings, and the deterministic master key.
    /// </summary>
    /// <remarks>
    /// The Keycloak hosts use <c>.invalid</c> (RFC 2606), which fails DNS resolution instantly.
    /// A <c>.local</c> host triggers mDNS/Avahi resolution that hangs ~15s before failing, adding
    /// ~20s to every test that exercises an (intentionally unreachable) Keycloak.
    /// </remarks>
    public static IReadOnlyDictionary<string, string?> Shared { get; } = new Dictionary<string, string?>
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
        ["KEYCLOAK_PASSWORD_CHECK_CLIENT_ID"] = TestConstants.CheckClientId,
        ["KEYCLOAK_PASSWORD_CHECK_CLIENT_SECRET"] = TestConstants.CheckClientCredential,
        ["BFF_ENABLED"] = "true",
        ["BFF_REDIRECT_URI"] = "http://localhost/api/auth/bff/callback",
        ["BFF_ALLOWED_HOSTS"] = "orkyo.com,*.orkyo.com,localhost",
        ["BFF_COOKIE_SECURE"] = "false",
        // Satisfies the configuration validator's format check (valid base64, exactly 32 bytes).
        // No real encryption runs in integration tests so the value is never used operationally.
        ["ORKYO_MASTER_ENCRYPTION_KEY"] = TestConstants.MasterEncryptionKey,
    };
}
