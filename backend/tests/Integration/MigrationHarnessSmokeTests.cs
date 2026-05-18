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

    // Phase 1 - People Resources schema validation
    [Fact]
    public async Task TestTenant_ShouldContain_PersonProfilesTable()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await TableExistsAsync(conn, "person_profiles")).Should().BeTrue(
            "People Resources migration (1400) should create the person_profiles table");
    }

    [Fact]
    public async Task TestTenant_ResourceGroups_ShouldHave_DefaultAvailabilityPercentColumn()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await ColumnExistsAsync(conn, "resource_groups", "default_availability_percent")).Should().BeTrue(
            "People Resources migration (1400) should add default_availability_percent column");
    }

    [Fact]
    public async Task TestTenant_ResourceAssignments_ShouldHave_RoleColumn()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await ColumnExistsAsync(conn, "resource_assignments", "role")).Should().BeTrue(
            "People Resources migration (1400) should add role column to resource_assignments");
    }

    [Fact]
    public async Task TestTenant_ResourceAssignments_ShouldHave_NotesColumn()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await ColumnExistsAsync(conn, "resource_assignments", "notes")).Should().BeTrue(
            "People Resources migration (1400) should add notes column to resource_assignments");
    }

    // Departments + Job Titles schema validation (migration 1420)
    [Fact]
    public async Task TestTenant_ShouldContain_JobTitlesTable()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await TableExistsAsync(conn, "job_titles")).Should().BeTrue(
            "Departments + Job Titles migration (1420) should create the job_titles table");
    }

    [Fact]
    public async Task TestTenant_ShouldContain_DepartmentsTable()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await TableExistsAsync(conn, "departments")).Should().BeTrue(
            "Departments + Job Titles migration (1420) should create the departments table");
    }

    [Fact]
    public async Task TestTenant_PersonProfiles_ShouldHave_JobTitleId_And_DepartmentId()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await ColumnExistsAsync(conn, "person_profiles", "job_title_id")).Should().BeTrue(
            "migration 1420 should add job_title_id FK column to person_profiles");
        (await ColumnExistsAsync(conn, "person_profiles", "department_id")).Should().BeTrue(
            "migration 1420 should add department_id FK column to person_profiles");
    }

    [Fact]
    public async Task TestTenant_PersonProfiles_ShouldNotHave_LegacyFreeTextColumns()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await ColumnExistsAsync(conn, "person_profiles", "job_title")).Should().BeFalse(
            "migration 1420 (clean break) should drop the legacy job_title VARCHAR column");
        (await ColumnExistsAsync(conn, "person_profiles", "department")).Should().BeFalse(
            "migration 1420 (clean break) should drop the legacy department VARCHAR column");
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @t)",
            conn);
        cmd.Parameters.AddWithValue("t", tableName);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

    private static async Task<bool> ColumnExistsAsync(NpgsqlConnection conn, string tableName, string columnName)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t AND column_name = @c)",
            conn);
        cmd.Parameters.AddWithValue("t", tableName);
        cmd.Parameters.AddWithValue("c", columnName);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }
}
