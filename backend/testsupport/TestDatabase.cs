using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Migrator;
using Orkyo.Shared;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// The database bootstrap steps every test fixture repeats: whether an external server
/// serves the run, creating a database once, and a migration runner over a module set.
/// <see cref="ProductDatabaseFixtureBase{TProgram, TFactory}"/> and foundation's own fixtures use them.
/// </summary>
public static class TestDatabase
{
    /// <summary>
    /// True when an external server serves the run: <c>CI=true</c> with <c>ConnectionStrings__Postgres</c>
    /// set. That is the Postgres service container in a GitHub Actions job that declares one, or the
    /// server <c>./dev.sh check</c> starts locally. Without both, the fixture starts Testcontainers.
    /// </summary>
    public static bool UseCiDatabase =>
        Environment.GetEnvironmentVariable("CI") == "true"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ConfigKeys.ConnectionStringPostgresEnvVar));

    /// <summary>
    /// The port of the external server when <see cref="UseCiDatabase"/> is on. Host, user and
    /// password are fixed (<c>localhost</c>, <c>postgres</c>/<c>postgres</c>); the port comes from
    /// the configured connection string so a local run can keep its dev stack on 5432
    /// (<c>./dev.sh check</c> starts one server per repo on 5440-5442).
    /// </summary>
    public static int CiDatabasePort =>
        new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConfigKeys.ConnectionStringPostgresEnvVar)).Port;

    /// <summary>Creates <paramref name="database"/> through <paramref name="adminConnectionString"/> unless it already exists.</summary>
    public static async Task CreateIfMissingAsync(string adminConnectionString, string database)
    {
        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn);
        check.Parameters.AddWithValue("n", database);
        if (await check.ExecuteScalarAsync() is not null) return;
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", conn);
        await create.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A <see cref="MigrationRunner"/> over the modules <paramref name="registerModules"/> adds,
    /// logging warnings and above to the console.
    /// </summary>
    public static MigrationRunner BuildMigrationRunner(Action<IServiceCollection> registerModules)
    {
        ArgumentNullException.ThrowIfNull(registerModules);
        var services = new ServiceCollection()
            .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .AddOrkyoMigrationPlatform();
        registerModules(services);
        return services.BuildServiceProvider().GetRequiredService<MigrationRunner>();
    }
}
