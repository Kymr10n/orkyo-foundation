using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed tests for foundation-owned <see cref="UserManagementService"/>
/// using an in-foundation test connection factory. Focuses on the
/// initial-admin provisioning flow and global-status mutations — both touch
/// <c>control_plane.users</c> + <c>control_plane.tenant_memberships</c>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserManagementServiceIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public UserManagementServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureInitialAdminAsync_ShouldUpsertAdminMembership_WhenUserExists()
    {
        var service = BuildService();
        var tenantId = await CreateTenantAsync("ensure-admin");
        var org = BuildOrgContext(tenantId, "ensure-admin");
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        var userId = await CreateUserAsync(email);

        await service.EnsureInitialAdminAsync(org, email);

        var membership = await ReadMembershipAsync(tenantId, userId);
        membership.Should().NotBeNull();
        membership!.Value.role.Should().Be("admin");
        membership.Value.status.Should().Be("active");
    }

    [Fact]
    public async Task EnsureInitialAdminAsync_ShouldBeIdempotent_WhenCalledTwice()
    {
        var service = BuildService();
        var tenantId = await CreateTenantAsync("idem-admin");
        var org = BuildOrgContext(tenantId, "idem-admin");
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        var userId = await CreateUserAsync(email);

        await service.EnsureInitialAdminAsync(org, email);
        await service.EnsureInitialAdminAsync(org, email);

        var count = await CountMembershipsAsync(tenantId, userId);
        count.Should().Be(1, "upsert should not duplicate membership rows");
    }

    [Fact]
    public async Task EnsureInitialAdminAsync_ShouldBeNoOp_WhenUserNotFound()
    {
        var service = BuildService();
        var tenantId = await CreateTenantAsync("missing-admin");
        var org = BuildOrgContext(tenantId, "missing-admin");
        var email = $"ghost-{Guid.NewGuid():N}@example.com";

        await service.EnsureInitialAdminAsync(org, email);

        var membershipsInTenant = await CountAllMembershipsAsync(tenantId);
        membershipsInTenant.Should().Be(0, "no user → no membership created");
    }

    [Fact]
    public async Task SetGlobalStatusAsync_ShouldUpdate_UsersStatusColumn()
    {
        var service = BuildService();
        var email = $"status-{Guid.NewGuid():N}@example.com";
        var userId = await CreateUserAsync(email);

        await service.SetGlobalStatusAsync(userId, "disabled");

        var status = await ReadUserStatusAsync(userId);
        status.Should().Be("disabled");
    }

    [Fact]
    public async Task PermanentlyDeleteAsync_ShouldRemoveUserRow_AndCascadeMemberships()
    {
        var service = BuildService();
        var tenantId = await CreateTenantAsync("hard-delete");
        var org = BuildOrgContext(tenantId, "hard-delete");
        var email = $"delete-{Guid.NewGuid():N}@example.com";
        var userId = await CreateUserAsync(email);
        await service.EnsureInitialAdminAsync(org, email);
        (await CountMembershipsAsync(tenantId, userId)).Should().Be(1, "precondition: membership created");

        await service.PermanentlyDeleteAsync(userId);

        var userExists = await UserExistsAsync(userId);
        userExists.Should().BeFalse();
        var membershipsAfter = await CountMembershipsAsync(tenantId, userId);
        membershipsAfter.Should().Be(0, "FK cascade should remove tenant_memberships");
    }

    // ── composition ──────────────────────────────────────────────────────────

    private UserManagementService BuildService()
    {
        var factory = _fixture.CreateConnectionFactory();
        var tenantUserService = new TenantUserService(factory, NullLogger<TenantUserService>.Instance);
        return new UserManagementService(factory, tenantUserService, NullLogger<UserManagementService>.Instance);
    }


    private OrgContext BuildOrgContext(Guid tenantId, string slug) => new()
    {
        OrgId = tenantId,
        OrgSlug = slug,
        DbConnectionString = _fixture.TestTenantConnectionString,
    };

    // ── DB helpers ───────────────────────────────────────────────────────────

    private async Task<Guid> CreateTenantAsync(string slugPrefix)
    {
        var tenantId = Guid.NewGuid();
        var slug = $"{slugPrefix}-{Guid.NewGuid():N}".Substring(0, 20);
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (id, slug, display_name, db_identifier, status) VALUES (@id, @slug, @name, @db, 'active')",
            conn);
        cmd.Parameters.AddWithValue("id", tenantId);
        cmd.Parameters.AddWithValue("slug", slug);
        cmd.Parameters.AddWithValue("name", $"Test {slug}");
        cmd.Parameters.AddWithValue("db", $"tenant_{slug}");
        await cmd.ExecuteNonQueryAsync();
        return tenantId;
    }

    private async Task<Guid> CreateUserAsync(string email)
    {
        var userId = Guid.NewGuid();
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, status) VALUES (@id, @email, 'active')", conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task<(string role, string status)?> ReadMembershipAsync(Guid tenantId, Guid userId)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT role, status FROM tenant_memberships WHERE tenant_id = @t AND user_id = @u", conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("u", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetString(1));
    }

    private async Task<long> CountMembershipsAsync(Guid tenantId, Guid userId)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM tenant_memberships WHERE tenant_id = @t AND user_id = @u", conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        cmd.Parameters.AddWithValue("u", userId);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> CountAllMembershipsAsync(Guid tenantId)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM tenant_memberships WHERE tenant_id = @t", conn);
        cmd.Parameters.AddWithValue("t", tenantId);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<string?> ReadUserStatusAsync(Guid userId)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT status FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private async Task<bool> UserExistsAsync(Guid userId)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM users WHERE id = @id)", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }
}
