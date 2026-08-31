using System.Threading.RateLimiting;
using ModelContextProtocol;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// The CPU ceiling on auto-scheduling, which the HTTP rate limiter cannot express.
///
/// <c>RequireRateLimiting</c> binds to an endpoint group, and MCP routes every tool through one
/// POST, so a per-tool limit has nowhere to attach there. That matters because a solve is not like
/// the other tools: <c>OrToolsSchedulingSolver</c> pins a core for up to five seconds, while a list
/// call costs a query.
///
/// Two limiters, because each closes what the other leaves open:
/// <list type="bullet">
/// <item>The <b>concurrency</b> limiter bounds how much of the box solving can take at once. The
/// existing 30/min MCP policy would otherwise let one token ask for 150 CPU-seconds per minute
/// against the 120 two permits provide — enough to starve every other tenant in the process.</item>
/// <item>The <b>per-tenant quota</b> decides who gets refused. Concurrency alone would push the
/// refusals onto whoever arrived second rather than onto the tenant causing the load.</item>
/// </list>
///
/// Order matters: the concurrency slot is taken first and handed back if the quota then refuses, so
/// a tenant over quota never holds a slot, and a tenant refused because the box is busy is not
/// charged for a solve it did not get.
/// </summary>
public sealed class McpSolveThrottle : IDisposable
{
    private readonly ConcurrencyLimiter _concurrency;
    private readonly PartitionedRateLimiter<Guid> _perTenant;

    /// <param name="maxConcurrentSolves">Solves that may run at once across the whole process.</param>
    /// <param name="solvesPerTenantPerMinute">Solves one tenant may start in a fixed minute.</param>
    public McpSolveThrottle(int maxConcurrentSolves = 2, int solvesPerTenantPerMinute = 6)
    {
        // QueueLimit 0 on both: refuse immediately rather than hold an HTTP request open. An agent
        // reading "busy, retry" acts sensibly; an agent waiting on a silent socket does not.
        _concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = maxConcurrentSolves,
            QueueLimit = 0,
        });

        _perTenant = PartitionedRateLimiter.Create<Guid, Guid>(tenantId =>
            RateLimitPartition.GetFixedWindowLimiter(tenantId, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = solvesPerTenantPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            }));
    }

    /// <summary>
    /// Test seam: the leak this class must never have — a concurrency slot stranded when the
    /// per-tenant acquire throws — needs a limiter that throws on cue, which the real fixed
    /// window cannot be made to do deterministically.
    /// </summary>
    internal McpSolveThrottle(int maxConcurrentSolves, PartitionedRateLimiter<Guid> perTenant)
    {
        _concurrency = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = maxConcurrentSolves,
            QueueLimit = 0,
        });
        _perTenant = perTenant;
    }

    /// <summary>
    /// Reserves capacity for one solve. Dispose the result when the solve finishes. Throws
    /// <see cref="McpException"/> with a cause-specific message — "busy" and "over quota" call for
    /// different behaviour from the agent, so they must not read the same.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(Guid tenantId, CancellationToken ct = default)
    {
        var slot = await _concurrency.AcquireAsync(1, ct);
        if (!slot.IsAcquired)
        {
            slot.Dispose();
            throw new McpException(
                "The scheduler is busy with other solves right now. Nothing was changed. "
                + "Retry in a few seconds.");
        }

        RateLimitLease quota;
        try
        {
            quota = await _perTenant.AcquireAsync(tenantId, 1, ct);
        }
        catch
        {
            // The slot is already held while this second acquire awaits. If it throws — a client
            // disconnect cancelling `ct` is the realistic case — the slot must go back, or two
            // such races permanently exhaust the two concurrency permits and auto-scheduling dies
            // process-wide, misreported as "the scheduler is busy".
            slot.Dispose();
            throw;
        }

        if (!quota.IsAcquired)
        {
            // Hand the slot back before refusing: the box is not busy, this caller is over quota.
            quota.Dispose();
            slot.Dispose();
            throw new McpException(
                "This workspace has run too many auto-schedules in the last minute. Nothing was "
                + "changed. Wait for the minute to roll over, then retry.");
        }

        return new SolveLease(slot, quota);
    }

    public void Dispose()
    {
        _concurrency.Dispose();
        _perTenant.Dispose();
    }

    /// <summary>Releases both reservations together, so neither can be leaked without the other.</summary>
    private sealed class SolveLease(RateLimitLease slot, RateLimitLease quota) : IDisposable
    {
        public void Dispose()
        {
            // The fixed-window permit is not returned by disposal — a solve started is a solve
            // counted. Only the concurrency permit comes back.
            quota.Dispose();
            slot.Dispose();
        }
    }
}
