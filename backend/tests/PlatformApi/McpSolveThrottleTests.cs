using System.Threading.RateLimiting;
using Api.PlatformApi.Mcp;
using AwesomeAssertions;
using ModelContextProtocol;
using Xunit;

namespace Orkyo.Foundation.Tests.PlatformApi;

/// <summary>
/// The CPU ceiling on solving. Both limiters are exercised, because each exists to cover what the
/// other cannot: concurrency bounds how much of the box solving takes, the per-tenant quota decides
/// which caller gets refused when it does.
/// </summary>
public class McpSolveThrottleTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public async Task AcquiresWhenThereIsCapacity()
    {
        using var throttle = new McpSolveThrottle();

        using var lease = await throttle.AcquireAsync(TenantA);

        lease.Should().NotBeNull();
    }

    [Fact]
    public async Task RefusesWhenEveryConcurrentSlotIsHeld()
    {
        using var throttle = new McpSolveThrottle(maxConcurrentSolves: 1, solvesPerTenantPerMinute: 100);
        using var held = await throttle.AcquireAsync(TenantA);

        var thrown = await Assert.ThrowsAsync<McpException>(() => throttle.AcquireAsync(TenantB));

        thrown.Message.Should().Contain("busy");
        // "Nothing was changed" matters: the agent must not assume a partial solve landed.
        thrown.Message.Should().Contain("Nothing was changed");
    }

    [Fact]
    public async Task ReleasingASlotLetsTheNextSolveThrough()
    {
        using var throttle = new McpSolveThrottle(maxConcurrentSolves: 1, solvesPerTenantPerMinute: 100);

        (await throttle.AcquireAsync(TenantA)).Dispose();

        using var next = await throttle.AcquireAsync(TenantB);
        next.Should().NotBeNull();
    }

    [Fact]
    public async Task RefusesATenantThatExceedsItsQuota()
    {
        using var throttle = new McpSolveThrottle(maxConcurrentSolves: 10, solvesPerTenantPerMinute: 2);

        (await throttle.AcquireAsync(TenantA)).Dispose();
        (await throttle.AcquireAsync(TenantA)).Dispose();

        var thrown = await Assert.ThrowsAsync<McpException>(() => throttle.AcquireAsync(TenantA));

        thrown.Message.Should().Contain("too many auto-schedules");
        thrown.Message.Should().Contain("Nothing was changed");
    }

    [Fact]
    public async Task TheQuotaIsPerTenant_SoOneWorkspaceCannotSpendAnothers()
    {
        // The whole reason the quota exists alongside the concurrency limiter: without it a busy
        // tenant's refusals would land on everyone else.
        using var throttle = new McpSolveThrottle(maxConcurrentSolves: 10, solvesPerTenantPerMinute: 1);
        (await throttle.AcquireAsync(TenantA)).Dispose();

        using var other = await throttle.AcquireAsync(TenantB);

        other.Should().NotBeNull();
        await Assert.ThrowsAsync<McpException>(() => throttle.AcquireAsync(TenantA));
    }

    [Fact]
    public async Task AQuotaRefusalDoesNotStrandAConcurrencySlot()
    {
        // The slot is taken before the quota is checked, so it must be handed back on refusal —
        // otherwise a tenant hammering its quota would drain the shared permits it never used.
        using var throttle = new McpSolveThrottle(maxConcurrentSolves: 1, solvesPerTenantPerMinute: 1);
        (await throttle.AcquireAsync(TenantA)).Dispose();

        await Assert.ThrowsAsync<McpException>(() => throttle.AcquireAsync(TenantA));

        // The only slot is still free for someone else.
        using var other = await throttle.AcquireAsync(TenantB);
        other.Should().NotBeNull();
    }

    [Fact]
    public async Task TheTwoRefusalsReadDifferently()
    {
        // They call for different agent behaviour — retry shortly versus wait for the window — so
        // collapsing them into one message would be a real loss of information.
        using var busy = new McpSolveThrottle(maxConcurrentSolves: 1, solvesPerTenantPerMinute: 100);
        using var held = await busy.AcquireAsync(TenantA);
        var busyMessage = (await Assert.ThrowsAsync<McpException>(
            () => busy.AcquireAsync(TenantB))).Message;

        using var capped = new McpSolveThrottle(maxConcurrentSolves: 10, solvesPerTenantPerMinute: 1);
        (await capped.AcquireAsync(TenantA)).Dispose();
        var quotaMessage = (await Assert.ThrowsAsync<McpException>(
            () => capped.AcquireAsync(TenantA))).Message;

        busyMessage.Should().NotBe(quotaMessage);
    }

    /// <summary>Throws on the first N acquires, then delegates to an always-permit lease.</summary>
    private sealed class ThrowingThenOpenLimiter(int throwCount) : PartitionedRateLimiter<Guid>
    {
        private int _remaining = throwCount;

        public override RateLimiterStatistics? GetStatistics(Guid resource) => null;

        protected override RateLimitLease AttemptAcquireCore(Guid resource, int permitCount)
            => throw new NotSupportedException();

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            Guid resource, int permitCount, CancellationToken cancellationToken)
        {
            if (_remaining-- > 0)
                throw new OperationCanceledException("client disconnected between the acquires");
            return ValueTask.FromResult<RateLimitLease>(new OpenLease());
        }

        private sealed class OpenLease : RateLimitLease
        {
            public override bool IsAcquired => true;
            public override IEnumerable<string> MetadataNames => [];
            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }
        }
    }

    [Fact]
    public async Task ACancelledQuotaAcquire_HandsTheConcurrencySlotBack()
    {
        // The race this guards: the slot is held while the per-tenant acquire awaits; a client
        // disconnect cancels that await. Leaking the slot here twice would exhaust the two
        // process-wide permits and kill auto-scheduling until restart, misreported as "busy".
        using var throttle = new McpSolveThrottle(
            maxConcurrentSolves: 1, perTenant: new ThrowingThenOpenLimiter(throwCount: 1));

        await Assert.ThrowsAsync<OperationCanceledException>(() => throttle.AcquireAsync(TenantA));

        // The single slot must be free again — without the fix this reports "busy".
        using var lease = await throttle.AcquireAsync(TenantA);
        lease.Should().NotBeNull();
    }
}
