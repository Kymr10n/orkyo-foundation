using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Migrator;
using Orkyo.Shared;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// The database bootstrap steps every test fixture repeats: whether the CI service container
/// serves the run, creating a database once, and a migration runner over a module set.
/// <see cref="ProductDatabaseFixtureBase{TProgram, TFactory}"/> and foundation's own fixtures use them.
/// </summary>
public static class TestDatabase
{
    /// <summary>True when the CI service container on port 5432 serves the run (<c>CI=true</c> with <c>ConnectionStrings__Postgres</c> set).</summary>
    public static bool UseCiDatabase =>
        Environment.GetEnvironmentVariable("CI") == "true"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ConfigKeys.ConnectionStringPostgresEnvVar));

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
