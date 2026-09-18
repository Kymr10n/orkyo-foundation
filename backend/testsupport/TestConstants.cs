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
    public static string BearerTokenForRole(string role) => BearerToken(
        userId: "11111111-1111-1111-1111-111111111111",
        email: "test@orkyo.example",
        displayName: "Test User",
        tenantId: "00000000-0000-0000-0000-000000000001",
        tenantSlug: TenantSlug,
        isTenantAdmin: false,
        role: role);

    /// <summary>
    /// Builds a Bearer token for an arbitrary test identity. The payload is the JSON shape
    /// <see cref="TestAuthHandler"/> decodes, so a test that needs a distinct user, tenant
    /// or Keycloak subject composes it here instead of hand-serialising the same object.
    /// </summary>
    /// <param name="sub">Keycloak subject claim; omitted from the token when null.</param>
    /// <param name="sid">Keycloak session id claim; omitted from the token when null.</param>
    /// <param name="realmRoles">Realm roles emitted as a <c>realm_access</c> claim; omitted when null or empty.</param>
    public static string BearerToken(
        string userId,
        string email,
        string displayName,
        string tenantId,
        string tenantSlug,
        bool isTenantAdmin,
        string role,
        string? sub = null,
        string? sid = null,
        string[]? realmRoles = null) => Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                UserId = userId,
                Email = email,
                DisplayName = displayName,
                TenantId = tenantId,
                TenantSlug = tenantSlug,
                IsTenantAdmin = isTenantAdmin,
                Role = role,
                Sub = sub,
                Sid = sid,
                RealmRoles = realmRoles
            })));
}
