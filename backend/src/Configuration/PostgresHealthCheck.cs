using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Api.Configuration;

/// <summary>
/// Readiness probe for a Postgres database: opens a connection and runs <c>SELECT 1</c>.
///
/// Replaces the third-party AspNetCore.HealthChecks.NpgSql package, which both products
/// carried for this single call. The probe is four lines of Npgsql — a dependency that is
/// already the entire data layer — so the package bought nothing but a supply-chain edge
/// and a major version (9.x) trailing the rest of the stack.
/// </summary>
internal sealed class PostgresHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

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
/// connection string, because control-plane (SaaS) and single-tenant (Community) resolve it
/// from different configuration keys.
/// </summary>
public static class OrkyoHealthCheckExtensions
{
    /// <summary>
    /// Adds a Postgres readiness probe. Tag it <c>ready</c> to have
    /// <c>MapOrkyoHealthEndpoints</c> include it in <c>/health/ready</c>.
    /// </summary>
    public static IHealthChecksBuilder AddPostgresCheck(
        this IHealthChecksBuilder builder,
        string connectionString,
        string name,
        params string[] tags)
        => builder.AddCheck(name, new PostgresHealthCheck(connectionString), failureStatus: null, tags: tags);
}
