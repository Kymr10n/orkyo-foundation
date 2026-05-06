using Npgsql;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// Provides helper methods for test data management.
/// Database migrations are handled automatically by <see cref="FoundationWebApplicationFactory"/>.
/// </summary>
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

        await using (var cmd = new NpgsqlCommand(
            @"DELETE FROM audit_events WHERE actor_user_id IN (
                SELECT user_id FROM tenant_memberships WHERE tenant_id = @tenantId
              )", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM invitations WHERE tenant_id = @tenantId", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        var userIdsToDelete = new List<Guid>();
        await using (var cmd = new NpgsqlCommand(
            @"SELECT tm.user_id FROM tenant_memberships tm
              WHERE tm.tenant_id = @tenantId
              AND NOT EXISTS (
                  SELECT 1 FROM tenant_memberships tm2
                  WHERE tm2.user_id = tm.user_id AND tm2.tenant_id != @tenantId
              )", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                userIdsToDelete.Add(reader.GetGuid(0));
        }

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM tenant_memberships WHERE tenant_id = @tenantId", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var userId in userIdsToDelete)
        {
            await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @userId", conn);
            cmd.Parameters.AddWithValue("userId", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM tenants WHERE id = @tenantId", conn))
        {
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public static async Task ActivateUserByEmail(string email)
    {
        await using var conn = new NpgsqlConnection(TestControlPlaneConnectionString);
        await conn.OpenAsync();

        await using var updateUserCmd = new NpgsqlCommand(
            "UPDATE users SET status = 'active', updated_at = NOW() WHERE email = @email", conn);
        updateUserCmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
        var updateCount = await updateUserCmd.ExecuteNonQueryAsync();

        await using var updateMembershipCmd = new NpgsqlCommand(
            @"UPDATE tenant_memberships tm
              SET status = 'active', updated_at = NOW()
              FROM users u
              WHERE tm.user_id = u.id AND u.email = @email", conn);
        updateMembershipCmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
        await updateMembershipCmd.ExecuteNonQueryAsync();

        if (updateCount == 0)
            throw new InvalidOperationException($"User {email} not found.");
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

    public static async Task<Guid> GetUserIdByEmail(string email)
    {
        await using var conn = new NpgsqlConnection(TestControlPlaneConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT id FROM users WHERE email = @email", conn);
        cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());

        var result = await cmd.ExecuteScalarAsync();
        if (result == null)
            throw new InvalidOperationException($"User with email {email} not found");

        return (Guid)result;
    }

    public static async Task MakeUserAdminByEmail(string email)
    {
        await using var conn = new NpgsqlConnection(TestControlPlaneConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            @"UPDATE tenant_memberships tm
              SET role = 'admin', updated_at = NOW()
              FROM users u
              WHERE tm.user_id = u.id AND u.email = @email", conn);
        cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());

        var updateCount = await cmd.ExecuteNonQueryAsync();
        if (updateCount == 0)
            throw new InvalidOperationException($"User {email} not found or no tenant membership.");
    }
}
