using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Smoke tests for the <see cref="PostgresFixture"/> itself: verify the container boots,
/// foundation migrations apply end-to-end, and both databases expose the expected
/// canonical foundation tables. If these pass, downstream integration tests can assume
/// a working foundation-migrated schema.
/// </summary>
/// <remarks>
/// This fixture only loads the foundation migration set. SaaS-owned tables
/// (<c>tenants</c>, <c>tenant_memberships</c>, <c>service_tier</c>) are intentionally
/// absent — those smoke tests live in <c>orkyo-saas/backend/tests</c>.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class MigrationHarnessSmokeTests
{
    private readonly PostgresFixture _fixture;

    public MigrationHarnessSmokeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ControlPlane_ShouldContain_UsersTable()
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        (await TableExistsAsync(conn, "users")).Should().BeTrue(
            "control-plane foundation migrations should create the users table");
    }

    [Fact]
    public async Task ControlPlane_ShouldContain_MigrationsTrackingTable()
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        (await TableExistsAsync(conn, "orkyo_schema_migrations")).Should().BeTrue(
            "the runner should record applied migrations in orkyo_schema_migrations");
    }

    [Fact]
    public async Task TestTenant_ShouldContain_SitesTable()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await TableExistsAsync(conn, "sites")).Should().BeTrue(
            "tenant foundation migrations should create the sites table");
    }

    [Fact]
    public async Task TestTenant_ShouldContain_SpacesTable()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await TableExistsAsync(conn, "spaces")).Should().BeTrue(
            "tenant foundation migrations should create the spaces table");
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @t)",
            conn);
        cmd.Parameters.AddWithValue("t", tableName);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }
}
