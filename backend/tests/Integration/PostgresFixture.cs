using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Foundation.Migrations;
using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;
using Testcontainers.PostgreSql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Integration-test Postgres fixture for the foundation repo: boots a containerized
/// PostgreSQL 16 instance and applies the foundation <see cref="IMigrationModule"/> set
/// (currently empty by design — see migration inventory) to a control-plane and a
/// tenant database.
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
    public const string ControlPlaneDatabase = "control_plane";
    public const string TestTenantDatabase = "test_tenant";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithUsername("orkyo")
        .WithPassword("orkyo-test")
        .WithDatabase("postgres")
        .Build();

    public string AdminConnectionString => _container.GetConnectionString();

    public string ControlPlaneConnectionString =>
        BuildConnectionString(AdminConnectionString, ControlPlaneDatabase);

    public string TestTenantConnectionString =>
        BuildConnectionString(AdminConnectionString, TestTenantDatabase);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await CreateDatabaseAsync(ControlPlaneDatabase);
        await CreateDatabaseAsync(TestTenantDatabase);

        var runner = BuildRunner();
        await runner.RunAsync(ControlPlaneConnectionString, MigrationTargetDatabase.ControlPlane,
            "orkyo:control-plane");
        await runner.RunAsync(TestTenantConnectionString, MigrationTargetDatabase.Tenant,
            $"orkyo:tenant:{TestTenantDatabase}");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public TestDbConnectionFactory CreateConnectionFactory() =>
        new(ControlPlaneConnectionString, TestTenantConnectionString, AdminConnectionString);

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

    private async Task CreateDatabaseAsync(string dbName)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn);
        check.Parameters.AddWithValue("n", dbName);
        if (await check.ExecuteScalarAsync() is not null) return;
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
        await create.ExecuteNonQueryAsync();
    }

    private static MigrationRunner BuildRunner()
    {
        var services = new ServiceCollection()
            .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .AddOrkyoMigrationPlatform()
            .AddFoundationMigrations()
            .BuildServiceProvider();
        return services.GetRequiredService<MigrationRunner>();
    }

    private static string BuildConnectionString(string baseConnectionString, string database) =>
        new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = database }.ConnectionString;
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
