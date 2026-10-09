using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Tests.Integration;
using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;

namespace Orkyo.Foundation.Tests.Migrations;

/// <summary>
/// <c>validate</c> answers "would this deploy succeed?" and must not change what it inspects:
/// only <c>migrate</c> creates a missing tenant database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationCliTests(PostgresFixture fixture)
{
    private sealed class OneTenant(string connectionString) : ITenantRegistry
    {
        public Task<IReadOnlyList<TenantDatabase>> ListActiveTenantsAsync(
            string controlPlaneConnectionString, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TenantDatabase>>([new TenantDatabase("t1", "t1", connectionString)]);
    }

    [Theory]
    [InlineData("validate", false)]
    [InlineData("migrate", true)]
    public async Task OnlyMigrate_CreatesAMissingTenantDatabase(string command, bool created)
    {
        var database = $"test_cli_{Guid.NewGuid():N}"[..24];
        var tenantConnection = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString) { Database = database }
            .ConnectionString;
        await using var services = new ServiceCollection()
            .AddSingleton(new MigrationRunner([]))
            .AddSingleton<ITenantRegistry>(new OneTenant(tenantConnection))
            .BuildServiceProvider();

        try
        {
            var exitCode = await services.RunMigrationCliAsync(
                [command, "--target", "tenant"], new MigrationCliOptions(fixture.ControlPlaneConnectionString));

            exitCode.Should().Be(0);
            (await DatabaseExistsAsync(database)).Should().Be(created);
        }
        finally
        {
            await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\"", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private async Task<bool> DatabaseExistsAsync(string database)
    {
        await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn);
        cmd.Parameters.AddWithValue("name", database);
        return await cmd.ExecuteScalarAsync() is not null;
    }
    private sealed class ManyTenants(IReadOnlyList<TenantDatabase> tenants) : ITenantRegistry
    {
        public Task<IReadOnlyList<TenantDatabase>> ListActiveTenantsAsync(
            string controlPlaneConnectionString, CancellationToken cancellationToken)
            => Task.FromResult(tenants);
    }

    private string TenantConnection(string database) =>
        new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString) { Database = database }.ConnectionString;

    private static string FreshDatabaseName() => $"test_cli_{Guid.NewGuid():N}"[..24];

    private async Task DropAsync(IEnumerable<string> databases)
    {
        await using var conn = new NpgsqlConnection(fixture.AdminConnectionString);
        await conn.OpenAsync();
        foreach (var database in databases)
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\"", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Six tenants, three at a time (saas#295): every database is created and the exit code is
    /// clean; the bound is the semaphore, not the tenant count.
    /// </summary>
    [Fact]
    public async Task Migrate_RunsTenantsInParallel_AndCreatesEveryDatabase()
    {
        var databases = Enumerable.Range(0, 6).Select(_ => FreshDatabaseName()).ToList();
        var tenants = databases.Select((db, i) => new TenantDatabase($"t{i}", $"t{i}", TenantConnection(db))).ToList();
        await using var services = new ServiceCollection()
            .AddSingleton(new MigrationRunner([]))
            .AddSingleton<ITenantRegistry>(new ManyTenants(tenants))
            .BuildServiceProvider();
        try
        {
            var exitCode = await services.RunMigrationCliAsync(
                ["migrate", "--target", "tenant"],
                new MigrationCliOptions(fixture.ControlPlaneConnectionString, TenantConcurrency: 3));

            exitCode.Should().Be(0);
            foreach (var database in databases)
                (await DatabaseExistsAsync(database)).Should().BeTrue(database);
        }
        finally
        {
            await DropAsync(databases);
        }
    }

    /// <summary>
    /// One unreachable tenant among reachable ones fails the run with exit code 1, the way
    /// the sequential loop did.
    /// </summary>
    [Fact]
    public async Task Migrate_FailsTheRun_WhenOneTenantIsUnreachable()
    {
        var databases = Enumerable.Range(0, 2).Select(_ => FreshDatabaseName()).ToList();
        var unreachable = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString)
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "nowhere",
            Timeout = 2,
        }.ConnectionString;
        var tenants = databases.Select((db, i) => new TenantDatabase($"ok{i}", $"ok{i}", TenantConnection(db)))
            .Append(new TenantDatabase("broken", "broken", unreachable)).ToList();
        await using var services = new ServiceCollection()
            .AddSingleton(new MigrationRunner([]))
            .AddSingleton<ITenantRegistry>(new ManyTenants(tenants))
            .BuildServiceProvider();
        try
        {
            var exitCode = await services.RunMigrationCliAsync(
                ["migrate", "--target", "tenant"],
                new MigrationCliOptions(fixture.ControlPlaneConnectionString, TenantConcurrency: 2));

            exitCode.Should().Be(1);
        }
        finally
        {
            await DropAsync(databases);
        }
    }

}
