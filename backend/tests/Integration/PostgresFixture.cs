using Npgsql;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Integration-test Postgres fixture for repository and service tests that need a schema
/// but no HTTP host: two databases of its own (control plane + tenant) on the server
/// <see cref="TestPostgresBootstrap"/> shares with <see cref="DatabaseFixture"/>, with the
/// full foundation <see cref="IMigrationModule"/> set applied to each. The databases are
/// separate from the endpoint suite's so neither collection sees the other's rows.
/// </summary>
/// <remarks>
/// The fixture intentionally does NOT load SaaS migrations: the test-placement rule
/// requires tests for SaaS-owned services to live in <c>orkyo-saas/backend/tests</c>.
/// Any test in this project that touches SaaS-owned tables (tenants, users, etc.)
/// is mis-placed and should be moved to the SaaS test project — leaving it here
/// will surface as a runtime "table does not exist" error against this fixture.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ControlPlaneDatabase = "control_plane_integration";
    public const string TestTenantDatabase = "test_tenant";

    private TestPostgresServer _server = null!;

    public string AdminConnectionString => _server.AdminConnectionString;

    public string ControlPlaneConnectionString => _server.ConnectionStringFor(ControlPlaneDatabase);

    public string TestTenantConnectionString => _server.ConnectionStringFor(TestTenantDatabase);

    public async Task InitializeAsync()
    {
        _server = await TestPostgresBootstrap.GetAsync();

        await TestPostgresBootstrap.EnsureDatabaseAsync(_server, ControlPlaneDatabase);
        await TestPostgresBootstrap.EnsureDatabaseAsync(_server, TestTenantDatabase);

        var runner = TestPostgresBootstrap.BuildFoundationRunner();
        await runner.RunAsync(ControlPlaneConnectionString, MigrationTargetDatabase.ControlPlane,
            "orkyo:control-plane");
        await runner.RunAsync(TestTenantConnectionString, MigrationTargetDatabase.Tenant,
            $"orkyo:tenant:{TestTenantDatabase}");

        // The same three types DatabaseFixture ensures. Migration 1880 removes them from a fresh
        // database, and the repository tests against this fixture look them up by key.
        await using var tenantConn = await OpenTestTenantConnectionAsync();
        await TestResourceTypes.EnsureAsync(tenantConn);
    }

    // The server outlives every fixture; see TestPostgresBootstrap.
    public Task DisposeAsync() => Task.CompletedTask;

    public TestDbConnectionFactory CreateConnectionFactory() =>
        new(ControlPlaneConnectionString, AdminConnectionString);

    public async Task<NpgsqlConnection> OpenControlPlaneConnectionAsync()
    {
        var conn = new NpgsqlConnection(ControlPlaneConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    public async Task<NpgsqlConnection> OpenTestTenantConnectionAsync()
    {
        var conn = new NpgsqlConnection(TestTenantConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
