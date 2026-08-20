using Api.Services;
using Api.Services.Insights;
using AwesomeAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The sharing that makes the Insights page affordable.
///
/// One page load asks for the conflict timeline once per report — the overview, the conflicts
/// trend, and one utilization chart per active resource type. On a workspace with nine types that
/// is eleven requests for one answer, each of which would otherwise run live conflict detection
/// over every scheduled request in the window. These pin that they collapse into one computation.
/// </summary>
public class CachingConflictTimelineProviderTests
{
    private static readonly DateTime From = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Counting(Func<Task>? gate = null) : IConflictTimelineProvider
    {
        public int Calls;

        public async Task<List<ConflictPoint>> GetAsync(
            DateTime from, DateTime to, Guid? siteId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            if (gate is not null) await gate();
            return [new ConflictPoint(from, "overlap")];
        }
    }

    private static CachingConflictTimelineProvider Wrap(IConflictTimelineProvider inner, Guid orgId) =>
        new(inner, new OrgContext { OrgId = orgId, OrgSlug = "test", DbConnectionString = "unused" });

    [Fact]
    public async Task ComputesOnceForRepeatedAsks()
    {
        var inner = new Counting();
        var provider = Wrap(inner, Guid.NewGuid());

        await provider.GetAsync(From, To, null);
        await provider.GetAsync(From, To, null);

        inner.Calls.Should().Be(1, "the second ask is answered from the cache");
    }

    [Fact]
    public async Task ElevenConcurrentAsksRunOneComputation()
    {
        // The page's actual shape: every report fires at once on a cold cache. Without
        // single-flight each of them would start its own conflict scan.
        var release = new TaskCompletionSource();
        var inner = new Counting(() => release.Task);
        var provider = Wrap(inner, Guid.NewGuid());

        var asks = Enumerable.Range(0, 11).Select(_ => provider.GetAsync(From, To, null)).ToList();
        release.SetResult();
        await Task.WhenAll(asks);

        inner.Calls.Should().Be(1, "eleven reports share one timeline");
        asks.Should().OnlyContain(a => a.Result.Count == 1);
    }

    [Fact]
    public async Task DoesNotShareAcrossSitesWindowsOrTenants()
    {
        // Everything the timeline actually depends on has to be in the key, or one site's
        // conflicts would be reported under another's.
        var inner = new Counting();
        var orgA = Guid.NewGuid();
        var provider = Wrap(inner, orgA);

        await provider.GetAsync(From, To, null);
        await provider.GetAsync(From, To, Guid.NewGuid());
        await provider.GetAsync(From, To.AddDays(1), null);
        await Wrap(inner, Guid.NewGuid()).GetAsync(From, To, null);

        inner.Calls.Should().Be(4);
    }
}
