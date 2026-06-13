namespace Orkyo.Foundation.Tests;

/// <summary>
/// Centralised constants for the integration test suite.
/// </summary>
public static class TestConstants
{
    /// <summary>Tenant slug used by all integration tests.</summary>
    public const string TenantSlug = "test";

    /// <summary>Name of the tenant-scoped PostgreSQL database created by <see cref="DatabaseFixture"/>.</summary>
    public const string TenantDatabase = "tenant_test";

    /// <summary>Base64-encoded 32-byte AES-256 master key for tests (deterministic, non-secret).</summary>
    public static string MasterEncryptionKey { get; } = Convert.ToBase64String(new byte[32]);

    /// <summary>
    /// Pre-encoded Bearer token for integration tests. Carries the default "user" role
    /// (which the factory treats as tenant Admin). Decoded by <see cref="TestAuthHandler"/>
    /// — no real JWT required.
    /// </summary>
    public static string TestBearerToken { get; } = BearerTokenForRole("user");

    /// <summary>
    /// Builds a Bearer token for the shared test user carrying a specific tenant
    /// <paramref name="role"/> ("admin" | "editor" | "viewer"). Used by authorization
    /// boundary tests to exercise role-gated endpoints.
    /// </summary>
    public static string BearerTokenForRole(string role) => Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                UserId = "11111111-1111-1111-1111-111111111111",
                Email = "test@orkyo.example",
                DisplayName = "Test User",
                TenantId = "00000000-0000-0000-0000-000000000001",
                TenantSlug = "test",
                IsTenantAdmin = false,
                Role = role
            })));
}
