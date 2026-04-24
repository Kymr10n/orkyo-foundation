using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Smoke tests for the <see cref="PostgresFixture"/> itself: verify the container boots,
/// migrations apply end-to-end, and both databases expose the expected canonical tables.
/// If these pass, downstream integration tests can assume a working migrated schema.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationHarnessSmokeTests
{
    private readonly PostgresFixture _fixture;

    public MigrationHarnessSmokeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ControlPlane_ShouldContain_TenantsTable()
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();

        var exists = await TableExistsAsync(conn, "tenants");

        exists.Should().BeTrue("control-plane migrations should create the tenants table");
    }

    [Fact]
    public async Task ControlPlane_ShouldContain_UsersTable()
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();

        var exists = await TableExistsAsync(conn, "users");

        exists.Should().BeTrue("control-plane migrations should create the users table");
    }

    [Fact]
    public async Task ControlPlane_ShouldContain_MigrationsTrackingTable()
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();

        var exists = await TableExistsAsync(conn, "_migrations");

        exists.Should().BeTrue("migration engine should create its tracking table");
    }

    [Fact]
    public async Task TestTenant_ShouldContain_SitesTable()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();

        var exists = await TableExistsAsync(conn, "sites");

        exists.Should().BeTrue("tenant migrations should create the sites table");
    }

    [Fact]
    public async Task TestTenant_ShouldContain_SpacesTable()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();

        var exists = await TableExistsAsync(conn, "spaces");

        exists.Should().BeTrue("tenant migrations should create the spaces table");
    }

    [Fact]
    public async Task ControlPlane_ShouldAcceptTenantInsert()
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        var slug = $"smoke-{Guid.NewGuid():N}".Substring(0, 20);

        await using var insert = new NpgsqlCommand(
            "INSERT INTO tenants (id, slug, display_name, db_identifier, status) VALUES (@id, @slug, @name, @db, 'active')",
            conn);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("slug", slug);
        insert.Parameters.AddWithValue("name", "Smoke Test");
        insert.Parameters.AddWithValue("db", $"tenant_{slug}");
        var rows = await insert.ExecuteNonQueryAsync();

        rows.Should().Be(1);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @t)",
            conn);
        cmd.Parameters.AddWithValue("t", tableName);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }
}
