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
    public async Task TestTenant_ShouldNotContain_PerTypeSideTables()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await TableExistsAsync(conn, "spaces")).Should().BeFalse(
            "migration 1710 folds spaces into resources — a resource type must not need a table");
        (await TableExistsAsync(conn, "person_profiles")).Should().BeFalse(
            "migration 1710 folds person_profiles into resources");
    }

    [Fact]
    public async Task TestTenant_Resources_ShouldCarry_TheFoldedProfileColumns()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        foreach (var column in new[]
                 { "code", "is_physical", "geometry", "properties", "capacity",
                   "email", "notes", "linked_user_id", "job_title_id", "department_id" })
        {
            (await ColumnExistsAsync(conn, "resources", column)).Should().BeTrue(
                $"migration 1700 should move {column} onto resources");
        }
    }

    [Fact]
    public async Task TestTenant_ResourceTypes_ShouldCarry_TheBehaviourFlags()
    {
        // These replace the hard-coded key = 'space' / 'person' tests in SQL and C#, which is
        // what lets a tenant-defined type opt into the same behaviour.
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        foreach (var flag in new[]
                 { "has_geometry", "has_directory_profile", "single_group_membership" })
        {
            (await ColumnExistsAsync(conn, "resource_types", flag)).Should().BeTrue(
                $"migration 1700 should add the {flag} flag");
        }
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
    public async Task TestTenant_Resources_ShouldHave_JobTitleId_And_DepartmentId()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await ColumnExistsAsync(conn, "resources", "job_title_id")).Should().BeTrue(
            "job titles are FK-modelled (1420), and the column now lives on resources (1700)");
        (await ColumnExistsAsync(conn, "resources", "department_id")).Should().BeTrue(
            "departments are FK-modelled (1420), and the column now lives on resources (1700)");
    }

    [Fact]
    public async Task TestTenant_Resources_ShouldNotHave_LegacyFreeTextColumns()
    {
        // The property 1420 established — job title and department are references, not free
        // text — must survive the fold onto resources, not be quietly reintroduced.
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        (await ColumnExistsAsync(conn, "resources", "job_title")).Should().BeFalse(
            "job title is an FK to job_titles, never a free-text VARCHAR");
        (await ColumnExistsAsync(conn, "resources", "department")).Should().BeFalse(
            "department is an FK to departments, never a free-text VARCHAR");
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
