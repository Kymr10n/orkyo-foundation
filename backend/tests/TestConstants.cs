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
    /// Pre-encoded Bearer token for integration tests.
    /// Decoded by <see cref="TestAuthHandler"/> — no real JWT required.
    /// </summary>
    public static string TestBearerToken { get; } = Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                UserId = "11111111-1111-1111-1111-111111111111",
                Email = "test@orkyo.example",
                DisplayName = "Test User",
                TenantId = "00000000-0000-0000-0000-000000000001",
                TenantSlug = "test",
                IsTenantAdmin = false,
                Role = "user"
            })));
}
