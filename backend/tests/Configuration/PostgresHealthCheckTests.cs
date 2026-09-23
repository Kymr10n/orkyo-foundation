using Api.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Orkyo.Foundation.Tests.Configuration;

/// <summary>
/// Tests for the Postgres readiness probe that replaced AspNetCore.HealthChecks.NpgSql.
///
/// The security-relevant invariant is the failure path: /health renders each entry's
/// Description into a payload that is scraped from outside the host, so a failing probe
/// must not disclose the server, database, user, or driver internals.
/// </summary>
[Collection("Database collection")]
public class PostgresHealthCheckTests(DatabaseFixture fixture)
{
    // A syntactically valid connection string pointing at a port nothing listens on, with
    // a short timeout so the failure path resolves quickly.
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=orkyo_probe_target;Username=probe_user;Password=probe_secret;Timeout=1;Command Timeout=1";

    private static HealthCheckContext ContextFor(string name, HealthStatus failureStatus = HealthStatus.Unhealthy)
        => new()
        {
            Registration = new HealthCheckRegistration(
                name,
                _ => throw new NotSupportedException("Not resolved in these tests."),
                failureStatus,
                tags: null),
        };

    [Fact]
    public async Task ReachableDatabase_ReportsHealthy()
    {
        // The success path is what gates a deploy: /health/ready must go green once Postgres
        // accepts connections, so it is exercised against the real test database rather than
        // asserted only in the negative.
        var connectionString =
            $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(dataSource, "postgres", "ready");
        var check = Resolve(services, "postgres");

        var result = await check.CheckHealthAsync(ContextFor("postgres"), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task UnreachableDatabase_ReportsFailureStatus()
    {
        await using var dataSource = NpgsqlDataSource.Create(UnreachableConnectionString);
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(dataSource, "postgres", "db", "ready");
        var check = Resolve(services, "postgres");

        var result = await check.CheckHealthAsync(ContextFor("postgres"), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task UnreachableDatabase_LeaksNoConnectionDetailInTheDescription()
    {
        await using var dataSource = NpgsqlDataSource.Create(UnreachableConnectionString);
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(dataSource, "postgres", "ready");
        var check = Resolve(services, "postgres");

        var result = await check.CheckHealthAsync(ContextFor("postgres"), CancellationToken.None);

        // Only the Description reaches the public /health body (the response writer renders
        // status/description/duration, never Exception), so it carries none of these.
        result.Description.Should().NotBeNull();
        result.Description!.Should().NotContain("orkyo_probe_target");
        result.Description!.Should().NotContain("probe_user");
        result.Description!.Should().NotContain("probe_secret");
        result.Description!.Should().NotContain("127.0.0.1");

        // The real cause is still available server-side for the framework to log.
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task RespectsTheRegisteredFailureStatus()
    {
        await using var dataSource = NpgsqlDataSource.Create(UnreachableConnectionString);
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(dataSource, "postgres");
        var check = Resolve(services, "postgres");

        // A product registering the probe as Degraded must not get Unhealthy back.
        var result = await check.CheckHealthAsync(
            ContextFor("postgres", HealthStatus.Degraded),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task UsesTheSharedDataSourceRatherThanAnIndependentConnection()
    {
        // The probe must open a logical connection from the application-owned data source
        // (and pool) rather than constructing its own NpgsqlConnection from a raw connection
        // string — that is what lets it inherit the application's real connection security
        // policy (for example GssEncryptionMode) instead of diverging from it just for /health.
        var connectionString =
            $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(dataSource, "postgres", "ready");
        var check = Resolve(services, "postgres");

        var first = await check.CheckHealthAsync(ContextFor("postgres"), CancellationToken.None);
        var second = await check.CheckHealthAsync(ContextFor("postgres"), CancellationToken.None);

        first.Status.Should().Be(HealthStatus.Healthy);
        second.Status.Should().Be(HealthStatus.Healthy);

        // The data source is still open and usable after the checks: the health check only
        // disposed the logical connections it borrowed from the pool, never the data source
        // itself, which is owned by the application's lifetime.
        await using var stillUsable = await dataSource.OpenConnectionAsync(CancellationToken.None);
        await using var command = stillUsable.CreateCommand();
        command.CommandText = "SELECT 1";
        (await command.ExecuteScalarAsync(CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public void RegistersUnderTheGivenNameAndTags()
    {
        using var dataSource = NpgsqlDataSource.Create(UnreachableConnectionString);
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(dataSource, "postgres", "db", "ready");

        var registration = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single();

        registration.Name.Should().Be("postgres");
        // "ready" is what MapOrkyoHealthEndpoints filters /health/ready on.
        registration.Tags.Should().BeEquivalentTo(["db", "ready"]);
    }

    private static IHealthCheck Resolve(IServiceCollection services, string name)
    {
        var provider = services.BuildServiceProvider();
        var registration = provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => r.Name == name);
        return registration.Factory(provider);
    }
}
