using Npgsql;

namespace Orkyo.Foundation.TestSupport;

public static class DatabaseTestUtils
{
    private static int _databasePort = 5433;

    public static void SetDatabasePort(int port) => _databasePort = port;

    private static string TestControlPlaneConnectionString =>
        $"Host=localhost;Port={_databasePort};Database=control_plane;Username=postgres;Password=postgres";

    private static string GetTenantConnectionString(string tenantSlug) =>
        $"Host=localhost;Port={_databasePort};Database=tenant_{tenantSlug};Username=postgres;Password=postgres";

    public static async Task<(Guid TenantId, string TenantSlug)> CreateTestTenantAsync(string? prefix = null)
    {
        var tenantId = Guid.NewGuid();
        var tenantSlug = $"{prefix ?? "test"}_{Guid.NewGuid().ToString()[..8]}";

        await using var conn = new NpgsqlConnection(TestControlPlaneConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO tenants (id, display_name, slug, db_identifier, status, created_at, updated_at)
              VALUES (@id, @displayName, @slug, @dbId, 'active', NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        cmd.Parameters.AddWithValue("displayName", $"Test Tenant {tenantSlug}");
        cmd.Parameters.AddWithValue("slug", tenantSlug);
        cmd.Parameters.AddWithValue("dbId", $"tenant_{tenantSlug}");
        await cmd.ExecuteNonQueryAsync();

        return (tenantId, tenantSlug);
    }

    public static async Task DeleteTestTenantAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(TestControlPlaneConnectionString);
        await conn.OpenAsync();

        foreach (var sql in new[]
        {
            "DELETE FROM audit_events WHERE actor_user_id IN (SELECT user_id FROM tenant_memberships WHERE tenant_id = @id)",
            "DELETE FROM invitations WHERE tenant_id = @id",
            "DELETE FROM tenant_memberships WHERE tenant_id = @id",
            "DELETE FROM tenants WHERE id = @id"
        })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task<Guid> CreateTestUserAsync(
        string email,
        string displayName = "Test User",
        string? tenantSlug = TestConstants.TenantSlug,
        string role = "viewer",
        bool active = false)
    {
        await using var conn = new NpgsqlConnection(TestControlPlaneConnectionString);
        await conn.OpenAsync();

        var userId = Guid.NewGuid();
        var status = active ? "active" : "pending_verification";

        await using var userCmd = new NpgsqlCommand(
            @"INSERT INTO users (id, email, display_name, status, created_at, updated_at)
              VALUES (@id, @email, @displayName, @status, NOW(), NOW())", conn);
        userCmd.Parameters.AddWithValue("id", userId);
        userCmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
        userCmd.Parameters.AddWithValue("displayName", displayName);
        userCmd.Parameters.AddWithValue("status", status);
        await userCmd.ExecuteNonQueryAsync();

        if (!string.IsNullOrEmpty(tenantSlug))
        {
            await using var tenantCmd = new NpgsqlCommand(
                "SELECT id FROM tenants WHERE slug = @slug", conn);
            tenantCmd.Parameters.AddWithValue("slug", tenantSlug);
            var tenantIdObj = await tenantCmd.ExecuteScalarAsync();

            if (tenantIdObj != null)
            {
                var tenantId = (Guid)tenantIdObj;
                var membershipStatus = active ? "active" : "pending";

                await using var membershipCmd = new NpgsqlCommand(
                    @"INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
                      VALUES (@userId, @tenantId, @role, @status, NOW(), NOW())
                      ON CONFLICT (user_id, tenant_id) DO UPDATE SET role = @role, status = @status", conn);
                membershipCmd.Parameters.AddWithValue("userId", userId);
                membershipCmd.Parameters.AddWithValue("tenantId", tenantId);
                membershipCmd.Parameters.AddWithValue("role", role);
                membershipCmd.Parameters.AddWithValue("status", membershipStatus);
                await membershipCmd.ExecuteNonQueryAsync();

                await using var tenantConn = new NpgsqlConnection(GetTenantConnectionString(tenantSlug));
                await tenantConn.OpenAsync();
                await using var tenantUserCmd = new NpgsqlCommand(
                    @"INSERT INTO users (id, email, display_name, created_at, synced_at)
                      VALUES (@id, @email, @displayName, NOW(), NOW())
                      ON CONFLICT (id) DO UPDATE SET email = @email, display_name = @displayName, synced_at = NOW()", tenantConn);
                tenantUserCmd.Parameters.AddWithValue("id", userId);
                tenantUserCmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
                tenantUserCmd.Parameters.AddWithValue("displayName", displayName);
                await tenantUserCmd.ExecuteNonQueryAsync();
            }
        }

        return userId;
    }

    /// <summary>
    /// Inserts an active control-plane user plus its Keycloak identity link (subject
    /// <c>kc-{userId}</c>), and returns a bearer token for that user carrying the <c>user</c>
    /// realm role (plus <c>site-admin</c> when <paramref name="siteAdmin"/>) and no tenant. This
    /// is the identity an admin-surface test needs: the link lets the context middleware resolve
    /// the caller, and the realm roles decide whether it is a site admin.
    /// </summary>
    /// <param name="prefix">Email prefix; the email is <c>{prefix}-{userId}@test.com</c>. Also
    /// the display name.</param>
    public static async Task<LinkedTestUser> CreateLinkedUserAsync(string prefix, bool siteAdmin = false)
    {
        var userId = Guid.NewGuid();
        var email = $"{prefix}-{userId}@test.com";
        var subject = $"kc-{userId}";
        var displayName = prefix;
        string[] realmRoles = siteAdmin ? ["user", "site-admin"] : ["user"];

        await using var conn = new NpgsqlConnection(TestControlPlaneConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, @displayName, 'active');
              INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email)
              VALUES (@identityId, @id, 'keycloak', @sub, @email)", conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("displayName", displayName);
        cmd.Parameters.AddWithValue("identityId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("sub", subject);
        await cmd.ExecuteNonQueryAsync();

        var token = TestConstants.BearerToken(
            userId: userId.ToString(),
            email: email,
            displayName: displayName,
            tenantId: Guid.Empty.ToString(),
            tenantSlug: "",
            isTenantAdmin: false,
            role: "viewer",
            sub: subject,
            realmRoles: realmRoles);
        return new LinkedTestUser(userId, token);
    }
}

/// <summary>A user created by <see cref="DatabaseTestUtils.CreateLinkedUserAsync"/>.</summary>
public sealed record LinkedTestUser(Guid UserId, string Token);
