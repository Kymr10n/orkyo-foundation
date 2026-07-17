namespace Orkyo.Foundation.TestSupport;

public static class TestConstants
{
    public const string TenantSlug = "test";
    public const string TenantDatabase = "tenant_test";

    /// <summary>Authentication scheme name registered by the test host + TestAuthHandler.</summary>
    public const string AuthScheme = "TestScheme";

    /// <summary>ASP.NET environment name used by the integration test host.</summary>
    public const string EnvironmentName = "Test";

    /// <summary>Base64-encoded 32-byte AES-256 master key for tests (deterministic, non-secret).</summary>
    public static string MasterEncryptionKey { get; } = Convert.ToBase64String(new byte[32]);

    /// <summary>
    /// Pre-encoded Bearer token for the shared test user carrying the default "user" role
    /// (which the factory treats as tenant Admin). Decoded by <see cref="TestAuthHandler"/>.
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
