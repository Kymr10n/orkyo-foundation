using Api.Models.Insights;
using Api.Services;
using Api.Services.Insights;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Unit tests for the short-TTL caching decorator over IInsightsService. Each test uses a fresh,
/// random OrgId so the process-wide static cache can't bleed between tests (and proves tenant
/// isolation: the cache key is scoped per org).
/// </summary>
public class CachingInsightsServiceTests
{
    private sealed class CountingInsights(InsightsOverview overview) : IInsightsService
    {
        public int OverviewCalls;

        public Task<InsightsOverview> GetOverviewAsync(InsightsFilter filter, CancellationToken ct = default)
        {
            OverviewCalls++;
            return Task.FromResult(overview);
        }

        public Task<InsightsUtilization> GetUtilizationTrendAsync(InsightsFilter filter, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<InsightsConflicts> GetConflictTrendAsync(InsightsFilter filter, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<InsightsRequests> GetRequestTrendAsync(InsightsFilter filter, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private static OrgContext Org(Guid id) => new() { OrgId = id, OrgSlug = "t", DbConnectionString = "x" };

    private static InsightsFilter Filter(DateTime from) => new() { From = from, To = from.AddDays(1) };

    private static InsightsOverview MakeOverview() => new()
    {
        Period = new InsightsPeriod { From = DateTime.UnixEpoch, To = DateTime.UnixEpoch.AddDays(1) },
        Requests = new RequestCounts { Total = 0, Scheduled = 0, Unscheduled = 0, Completed = 0, Cancelled = 0 },
        Conflicts = new ConflictCounts
        {
            Total = 0,
            Overbooking = 0,
            CriteriaMismatch = 0,
            ResourceUnavailable = 0,
            ScheduleOutsideAvailability = 0,
            MissingResource = 0,
        },
        Utilization = new UtilizationSummary { SpacesPercent = null, PeoplePercent = null, ToolsPercent = null },
        Metadata = new InsightsMetadata { CalculatedAt = DateTime.UtcNow, SourceMode = "live" },
    };

    [Fact]
    public async Task SecondCall_SameOrgAndFilter_ServedFromCache()
    {
        var overview = MakeOverview();
        var inner = new CountingInsights(overview);
        var sut = new CachingInsightsService(inner, Org(Guid.NewGuid()));
        var filter = Filter(new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var first = await sut.GetOverviewAsync(filter);
        var second = await sut.GetOverviewAsync(filter);

        Assert.Same(overview, first);
        Assert.Same(first, second);
        Assert.Equal(1, inner.OverviewCalls); // computed once, second served from cache
    }

    [Fact]
    public async Task DifferentFilter_Recomputes()
    {
        var inner = new CountingInsights(MakeOverview());
        var sut = new CachingInsightsService(inner, Org(Guid.NewGuid()));

        await sut.GetOverviewAsync(Filter(new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await sut.GetOverviewAsync(Filter(new DateTime(2032, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(2, inner.OverviewCalls);
    }

    [Fact]
    public async Task DifferentOrg_DoesNotShareCacheEntry()
    {
        var innerA = new CountingInsights(MakeOverview());
        var innerB = new CountingInsights(MakeOverview());
        var filter = Filter(new DateTime(2033, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var sutA = new CachingInsightsService(innerA, Org(Guid.NewGuid()));
        var sutB = new CachingInsightsService(innerB, Org(Guid.NewGuid()));

        await sutA.GetOverviewAsync(filter);
        await sutA.GetOverviewAsync(filter);
        await sutB.GetOverviewAsync(filter);

        Assert.Equal(1, innerA.OverviewCalls);
        Assert.Equal(1, innerB.OverviewCalls); // B's call is a miss under its own org key, not A's cached value
    }
}
