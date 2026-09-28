using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Foundation.Migrations;
using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;
using Testcontainers.PostgreSql;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// The one PostgreSQL server every fixture in this project shares. <see cref="DatabaseFixture"/>
/// and <see cref="Integration.PostgresFixture"/> each own their databases on it; the server
/// itself boots once per test run, on first use, and lives until the process exits.
/// </summary>
/// <remarks>
/// In CI (<c>CI=true</c> with <c>ConnectionStrings__Postgres</c> set) the service container on
/// port 5432 is used and nothing is started. Locally a Testcontainers instance is started; it
/// is disposed on process exit and, failing that, reaped by Testcontainers' resource reaper.
/// xunit runs the two collections in parallel, so database creation is serialised here.
/// </remarks>
public static class TestPostgresBootstrap
{
    private static readonly Lazy<Task<TestPostgresServer>> Server = new(StartAsync);
    private static readonly SemaphoreSlim DatabaseCreation = new(1, 1);

    /// <summary>Returns the shared server, starting it on the first call.</summary>
    public static Task<TestPostgresServer> GetAsync() => Server.Value;

    /// <summary>Creates <paramref name="database"/> on the shared server if it does not exist.</summary>
    public static async Task EnsureDatabaseAsync(TestPostgresServer server, string database)
    {
        await DatabaseCreation.WaitAsync();
        try
        {
            await TestDatabase.CreateIfMissingAsync(server.AdminConnectionString, database);
        }
        finally
        {
            DatabaseCreation.Release();
        }
    }

    /// <summary>
    /// A runner over the foundation migration set alone — the test-placement rule keeps
    /// product migrations out of this project.
    /// </summary>
    public static MigrationRunner BuildFoundationRunner() =>
        TestDatabase.BuildMigrationRunner(services => services.AddFoundationMigrations());

    private static async Task<TestPostgresServer> StartAsync()
    {
        if (TestDatabase.UseCiDatabase)
        {
            Console.WriteLine("⚡ CI detected — using service container on port 5432 (skipping Testcontainers)");
            return new TestPostgresServer(5432);
        }

        Console.WriteLine("🚀 Starting PostgreSQL test container...");
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithDatabase("postgres")
            .WithCleanUp(true)
            .Build();
        await container.StartAsync();

        var port = container.GetMappedPublicPort(5432);
        Console.WriteLine($"  ✓ PostgreSQL container started on port {port}");

        AppDomain.CurrentDomain.ProcessExit += (_, _) => container.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new TestPostgresServer(port);
    }
}

/// <summary>Connection details for the shared test server; one superuser role, many databases.</summary>
public sealed class TestPostgresServer
{
    public TestPostgresServer(int port)
    {
        Port = port;
        AdminConnectionString = ConnectionStringFor("postgres");
    }

    /// <summary>Host port the server listens on.</summary>
    public int Port { get; }

    /// <summary>Superuser connection to the maintenance database, for CREATE DATABASE.</summary>
    public string AdminConnectionString { get; }

    /// <summary>Superuser connection to <paramref name="database"/> on this server.</summary>
    public string ConnectionStringFor(string database) =>
        $"Host=localhost;Port={Port};Database={database};Username=postgres;Password=postgres";
}
