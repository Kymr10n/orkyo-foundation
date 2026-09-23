using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Api.Configuration;

/// <summary>
/// Readiness probe for a Postgres database: opens a logical connection from the
/// application's <see cref="NpgsqlDataSource"/> and runs <c>SELECT 1</c>.
///
/// Replaces the third-party AspNetCore.HealthChecks.NpgSql package, which both products
/// carried for this single call. The probe is four lines of Npgsql — a dependency that is
/// already the entire data layer — so the package bought nothing but a supply-chain edge
/// and a major version (9.x) trailing the rest of the stack.
///
/// The probe takes the same <see cref="NpgsqlDataSource"/> the product registers for its
/// repositories, rather than building its own <see cref="NpgsqlConnection"/> from a raw
/// connection string. That way the probe exercises the same pool and the same connection
/// security policy (for example <c>GssEncryptionMode</c>) as the application's real
/// database traffic — a product that needs to disable GSSAPI encryption negotiation
/// configures it once, on the shared <see cref="NpgsqlDataSourceBuilder"/>, not only for
/// this probe.
/// </summary>
internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // A logical connection from the pool — dispose only this, never the data source.
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        // A cancelled probe is not an unhealthy database — let it propagate.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The description is rendered into the public /health payload, which is scraped
            // from outside the host, so it stays generic: no server, database name, user, or
            // driver internals. The exception rides in HealthCheckResult.Exception, which the
            // response writer does not render and the framework logs server-side.
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                "Database is not reachable.",
                ex);
        }
    }
}

/// <summary>
/// Registration helper for the shared infrastructure health checks. The product supplies the
/// <see cref="NpgsqlDataSource"/>, because control-plane (SaaS) and single-tenant (Community)
/// resolve their connection strings from different configuration keys, and each registers its
/// own data source(s) for its repositories to share.
/// </summary>
public static class OrkyoHealthCheckExtensions
{
    /// <summary>
    /// Adds a Postgres readiness probe over the given <paramref name="dataSource"/>. Tag it
    /// <c>ready</c> to have <c>MapOrkyoHealthEndpoints</c> include it in <c>/health/ready</c>.
    /// The probe opens a logical connection from <paramref name="dataSource"/> per check; it
    /// never disposes the data source itself, so it shares the same pool and connection
    /// security policy (for example <c>GssEncryptionMode</c>) as the rest of the application.
    /// </summary>
    public static IHealthChecksBuilder AddPostgresCheck(
        this IHealthChecksBuilder builder,
        NpgsqlDataSource dataSource,
        string name,
        params string[] tags)
        => builder.AddCheck(name, new PostgresHealthCheck(dataSource), failureStatus: null, tags: tags);
}
