using Api.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(connectionString, "postgres", "ready");
        var check = Resolve(services, "postgres");

        var result = await check.CheckHealthAsync(ContextFor("postgres"), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task UnreachableDatabase_ReportsFailureStatus()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(UnreachableConnectionString, "postgres", "db", "ready");
        var check = Resolve(services, "postgres");

        var result = await check.CheckHealthAsync(ContextFor("postgres"), CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task UnreachableDatabase_LeaksNoConnectionDetailInTheDescription()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(UnreachableConnectionString, "postgres", "ready");
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
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(UnreachableConnectionString, "postgres");
        var check = Resolve(services, "postgres");

        // A product registering the probe as Degraded must not get Unhealthy back.
        var result = await check.CheckHealthAsync(
            ContextFor("postgres", HealthStatus.Degraded),
            CancellationToken.None);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void RegistersUnderTheGivenNameAndTags()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks().AddPostgresCheck(UnreachableConnectionString, "postgres", "db", "ready");

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
