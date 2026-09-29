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
}
