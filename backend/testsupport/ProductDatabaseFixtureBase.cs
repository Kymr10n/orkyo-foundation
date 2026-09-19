using Api.Constants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Migrator;

namespace Orkyo.Foundation.TestSupport;

/// <summary>
/// Base for a product's shared database fixture: resolves the PostgreSQL server (the CI service
/// container on port 5432, or one the product starts locally), lets the product create and
/// migrate its own databases, then boots the product's factory eagerly.
/// </summary>
/// <remarks>
/// <para>
/// This package carries no Testcontainers dependency, so the local server boot stays with the
/// product: <see cref="StartLocalServerAsync"/> returns the port and superuser connection string
/// of whatever it started, and <see cref="StopLocalServerAsync"/> tears it down. Both are skipped
/// in CI (<c>CI=true</c> with <c>ConnectionStrings__Postgres</c> set).
/// </para>
/// <para>
/// The package also carries no test-framework dependency. <see cref="InitializeAsync"/> and
/// <see cref="DisposeAsync"/> match xunit's <c>IAsyncLifetime</c> signatures, so the product's
/// subclass declares <c>IAsyncLifetime</c> itself and inherits the implementation.
/// </para>
/// </remarks>
/// <typeparam name="TProgram">The product's <c>Program</c> entry-point type.</typeparam>
/// <typeparam name="TFactory">The product's factory, usually a <see cref="ProductWebApplicationFactoryBase{TProgram}"/>.</typeparam>
public abstract class ProductDatabaseFixtureBase<TProgram, TFactory>
    where TProgram : class
    where TFactory : WebApplicationFactory<TProgram>
{
    /// <summary>Host port the test server listens on.</summary>
    public int DatabasePort { get; private set; }

    /// <summary>Superuser connection to the <c>postgres</c> maintenance database.</summary>
    public string AdminConnectionString { get; private set; } = "";

    /// <summary>The product's factory, started by <see cref="InitializeAsync"/>.</summary>
    public TFactory Factory { get; private set; } = null!;

    /// <summary>True when the CI service container on port 5432 is used instead of a local server.</summary>
    protected static bool UseCiDatabase =>
        Environment.GetEnvironmentVariable("CI") == "true"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres"));

    /// <summary>Starts the product's local PostgreSQL server (not called in CI).</summary>
    protected abstract Task<(int Port, string AdminConnectionString)> StartLocalServerAsync();

    /// <summary>Stops what <see cref="StartLocalServerAsync"/> started (not called in CI).</summary>
    protected abstract Task StopLocalServerAsync();

    /// <summary>
    /// Creates the product's databases, applies its migrations and seeds the shared test data.
    /// <see cref="CreateDatabaseAsync"/>, <see cref="BuildConnectionString"/> and
    /// <see cref="BuildMigrationRunner"/> are the building blocks.
    /// </summary>
    protected abstract Task CreateAndMigrateDatabasesAsync();

    /// <summary>Constructs the product's factory over this fixture.</summary>
    protected abstract TFactory CreateFactory();

    /// <summary>
    /// Runs after the databases are ready and before the factory starts. <c>Program.cs</c> reads
    /// some values from <c>builder.Configuration</c> before the factory's configuration callback
    /// runs; for those the only timing-safe override is the process environment, set here.
    /// </summary>
    protected virtual void BeforeFactoryStart()
    {
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the standard test tenant slug and
    /// bearer-token authorization headers preset.
    /// </summary>
    public HttpClient CreateAuthorizedClient(string tenantSlug = TestConstants.TenantSlug)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, tenantSlug);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {TestConstants.TestBearerToken}");
        return client;
    }

    public virtual async Task InitializeAsync()
    {
        if (UseCiDatabase)
        {
            DatabasePort = 5432;
            AdminConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
        }
        else
        {
            (DatabasePort, AdminConnectionString) = await StartLocalServerAsync();
        }

        DatabaseTestUtils.SetDatabasePort(DatabasePort);
        await CreateAndMigrateDatabasesAsync();

        // Both products read the master key in Program.cs before the factory's configuration
        // callback runs; the value is the same deterministic key the shared configuration carries.
        Environment.SetEnvironmentVariable("ORKYO_MASTER_ENCRYPTION_KEY", TestConstants.MasterEncryptionKey);
        BeforeFactoryStart();

        Factory = CreateFactory();
        _ = Factory.Services; // Eagerly start the server; CreateClient() no longer does this lazily in MvcTesting 10.x
    }

    public virtual async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (!UseCiDatabase)
            await StopLocalServerAsync();
    }

    /// <summary>The superuser connection string for <paramref name="database"/> on the test server.</summary>
    public string BuildConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = database }.ConnectionString;

    /// <summary>Creates <paramref name="dbName"/> unless it already exists.</summary>
    protected async Task CreateDatabaseAsync(string dbName)
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @n", conn);
        check.Parameters.AddWithValue("n", dbName);
        if (await check.ExecuteScalarAsync() is not null) return;
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
        await create.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// A <see cref="MigrationRunner"/> over the modules <paramref name="registerModules"/> adds
    /// (the product's <c>AddFoundationMigrations().AddXxxMigrations()</c> chain), logging warnings
    /// and above to the console.
    /// </summary>
    protected static MigrationRunner BuildMigrationRunner(Action<IServiceCollection> registerModules)
    {
        ArgumentNullException.ThrowIfNull(registerModules);
        var services = new ServiceCollection()
            .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .AddOrkyoMigrationPlatform();
        registerModules(services);
        return services.BuildServiceProvider().GetRequiredService<MigrationRunner>();
    }
}
