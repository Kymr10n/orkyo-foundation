using Api.Security.Quotas;

namespace Orkyo.Foundation.Tests.Security;

public class NoOpQuotaEnforcerTests
{
    private readonly NoOpQuotaEnforcer _enforcer = new();

    [Theory]
    [InlineData(QuotaResourceTypes.ActiveSeats, 0)]
    [InlineData(QuotaResourceTypes.ActiveSeats, 1_000_000)]
    [InlineData(QuotaResourceTypes.ProductionSites, 99)]
    [InlineData(QuotaResourceTypes.Spaces, 99)]
    [InlineData(QuotaResourceTypes.StorageBytes, 10_737_418_240)] // 10 GiB
    public async Task EnsureWithinLimitAsync_NeverThrows(string resourceType, long count)
    {
        await _enforcer.Invoking(e => e.EnsureWithinLimitAsync(resourceType, count, 1)).Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(QuotaResourceTypes.ActiveSeats)]
    [InlineData(QuotaResourceTypes.ProductionSites)]
    [InlineData(QuotaResourceTypes.Spaces)]
    [InlineData(QuotaResourceTypes.StorageBytes)]
    [InlineData("unknown-resource")]
    public async Task GetLimitAsync_AlwaysReturnsUnlimited(string resourceType)
    {
        var limit = await _enforcer.GetLimitAsync(resourceType);
        limit.Should().Be(-1L);
    }
}
