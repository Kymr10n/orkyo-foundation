using Npgsql;
using Orkyo.Migrations;
using Testcontainers.PostgreSql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Integration-test Postgres fixture: boots a containerized PostgreSQL 16 instance,
/// applies the foundation-owned control-plane migrations to the <c>control_plane</c> database
/// and tenant migrations to a single <c>test_tenant</c> database, and exposes connection
/// strings for both. Reusable across collections via <see cref="PostgresCollection"/>.
///
/// The fixture deliberately uses the existing <c>Orkyo.Migrations</c> engine rather than
/// a test-only schema-spinner: tests therefore exercise the same migration path as dev/prod,
/// providing regression coverage for the migrations themselves.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string ControlPlaneDatabase = "control_plane";
    public const string TestTenantDatabase = "test_tenant";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .WithUsername("orkyo")
        .WithPassword("orkyo-test")
        .WithDatabase("postgres")
        .Build();

    /// <summary>Admin connection string (to the <c>postgres</c> bootstrap DB).</summary>
    public string AdminConnectionString => _container.GetConnectionString();

    /// <summary>Connection string for the migrated <c>control_plane</c> database.</summary>
    public string ControlPlaneConnectionString =>
        MigrationEngine.BuildConnectionString(AdminConnectionString, ControlPlaneDatabase);

    /// <summary>Connection string for the migrated <c>test_tenant</c> database.</summary>
    public string TestTenantConnectionString =>
        MigrationEngine.BuildConnectionString(AdminConnectionString, TestTenantDatabase);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var migrationsRoot = MigrationEngine.FindMigrationsRoot();
        await MigrationEngine.MigrateDatabaseAsync(
            AdminConnectionString,
            ControlPlaneDatabase,
            Path.Combine(migrationsRoot, "control-plane"));

        await MigrationEngine.MigrateDatabaseAsync(
            AdminConnectionString,
            TestTenantDatabase,
            Path.Combine(migrationsRoot, "tenant"));
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Opens a new <see cref="NpgsqlConnection"/> to the control-plane database.
    /// The caller is responsible for disposal.
    /// </summary>
    public async Task<NpgsqlConnection> OpenControlPlaneConnectionAsync()
    {
        var conn = new NpgsqlConnection(ControlPlaneConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    /// <summary>
    /// Opens a new <see cref="NpgsqlConnection"/> to the migrated tenant database.
    /// The caller is responsible for disposal.
    /// </summary>
    public async Task<NpgsqlConnection> OpenTestTenantConnectionAsync()
    {
        var conn = new NpgsqlConnection(TestTenantConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

/// <summary>
/// xUnit collection marker: test classes tagged <c>[Collection(PostgresCollection.Name)]</c>
/// share a single <see cref="PostgresFixture"/> instance, so the container starts once
/// per test run instead of per test class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
