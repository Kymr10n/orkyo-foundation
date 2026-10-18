using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Api.Configuration;

/// <summary>
/// Readiness probe for Valkey: one <c>PING</c> over the process's multiplexer. Valkey holds the
/// BFF sessions and the rate-limit counters, so a backend without it answers 429 and signs
/// everyone out while a Postgres-only <c>/health</c> stayed green — the cutover and the uptime
/// probe passed right through that outage.
/// </summary>
internal sealed class ValkeyHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _valkey;

    public ValkeyHealthCheck(IConnectionMultiplexer valkey)
    {
        _valkey = valkey;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var roundTrip = await _valkey.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"PING {roundTrip.TotalMilliseconds:F0} ms");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Valkey PING failed", ex);
        }
    }
}
