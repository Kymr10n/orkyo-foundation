using Npgsql;
using Orkyo.Foundation.Migrations;
using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;
using Testcontainers.PostgreSql;

namespace Orkyo.Foundation.Tests.Reporting;

/// <summary>
/// Isolation-test fixture with two independent tenant databases.
///
/// Bootstraps control_plane + tenant_a + tenant_b, migrates all three, seeds
/// a single marker request in each tenant DB (name = the MARKER_* constants),
/// and grants LOGIN to both rpt_reader roles so tests can open real reader
/// connections to verify cross-tenant isolation.
/// </summary>
public sealed class TwoTenantFixture : IAsyncLifetime
{
    // Tenant DB names
    public const string TenantADb = "tenant_a";
    public const string TenantBDb = "tenant_b";

    // Marker values seeded into rpt_request_pipeline (planning_mode='leaf')
    public const string MarkerTenantA = "TENANT_A_MARKER_REQUEST";
    public const string MarkerTenantB = "TENANT_B_MARKER_REQUEST";

    // Password given to both reader roles for test-time direct connections
    private const string ReaderRolePassword = "test_reader_pw";

    private PostgreSqlContainer? _container;

    public int Port { get; private set; }

    public string ConnectionStringForDatabase(string dbName) =>
        $"Host=localhost;Port={Port};Database={dbName};Username=postgres;Password=postgres";

    public string ReaderConnectionString(string dbName) =>
        $"Host=localhost;Port={Port};Database={dbName};" +
        $"Username={dbName}_rpt_reader;Password={ReaderRolePassword}";

    private bool UseCiDatabase =>
        Environment.GetEnvironmentVariable("CI") == "true"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres"));

    public async Task InitializeAsync()
    {
        if (UseCiDatabase)
        {
            Port = 5432;
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithImage("postgres:16-alpine")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .WithCleanUp(true)
                .Build();
            await _container.StartAsync();
            Port = _container.GetMappedPublicPort(5432);
        }

        await CreateDatabasesAsync();
        await MigrateAllAsync();
        await GrantReaderLoginAsync(TenantADb);
        await GrantReaderLoginAsync(TenantBDb);
        await SeedTenantAsync(TenantADb, MarkerTenantA);
        await SeedTenantAsync(TenantBDb, MarkerTenantB);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // ── Setup helpers ─────────────────────────────────────────────────────────

    private async Task CreateDatabasesAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionStringForDatabase("postgres"));
        await conn.OpenAsync();

        foreach (var db in new[] { "control_plane", TenantADb, TenantBDb })
        {
            await using var checkCmd = new NpgsqlCommand(
                $"SELECT 1 FROM pg_database WHERE datname = '{db}'", conn);
            if (await checkCmd.ExecuteScalarAsync() == null)
            {
                await using var cmd = new NpgsqlCommand($"CREATE DATABASE {db}", conn);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private async Task MigrateAllAsync()
    {
        var runner = new MigrationRunner([new FoundationMigrationModule()]);

        await runner.RunAsync(
            ConnectionStringForDatabase("control_plane"),
            MigrationTargetDatabase.ControlPlane,
            "two-tenant-test-cp");

        foreach (var db in new[] { TenantADb, TenantBDb })
        {
            await runner.RunAsync(
                ConnectionStringForDatabase(db),
                MigrationTargetDatabase.Tenant,
                db);
        }
    }

    /// <summary>
    /// Grant LOGIN + password to the {db}_rpt_reader role so tests can open a
    /// direct connection as that role.  The migration creates the role NOLOGIN;
    /// this mirrors what TenantReportingProvisioner does at provisioning time.
    /// </summary>
    private async Task GrantReaderLoginAsync(string dbName)
    {
        var roleName = $"{dbName}_rpt_reader";
        // Role is cluster-scoped, so ALTER ROLE runs against any DB.
        await using var conn = new NpgsqlConnection(ConnectionStringForDatabase(dbName));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"ALTER ROLE \"{roleName}\" LOGIN PASSWORD '{ReaderRolePassword}'", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds one request with <paramref name="markerName"/> as its name.
    /// rpt_request_pipeline selects on planning_mode='leaf' — no other joins needed.
    /// </summary>
    private async Task SeedTenantAsync(string dbName, string markerName)
    {
        await using var conn = new NpgsqlConnection(ConnectionStringForDatabase(dbName));
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO public.requests
                (id, name, minimal_duration_value, minimal_duration_unit, planning_mode, status, sort_order)
            VALUES
                (gen_random_uuid(), @name, 60, 'minutes', 'leaf', 'planned', 0)
            ON CONFLICT DO NOTHING", conn);
        cmd.Parameters.AddWithValue("name", markerName);
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("TwoTenant collection")]
public sealed class TwoTenantCollection : ICollectionFixture<TwoTenantFixture>;
