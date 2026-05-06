using Api.Security.Quotas;

namespace Orkyo.Foundation.Tests.Security;

public class NoOpQuotaEnforcerTests
{
    private readonly NoOpQuotaEnforcer _enforcer = new();

    [Theory]
    [InlineData(QuotaResourceTypes.ActiveSeats, 0)]
    [InlineData(QuotaResourceTypes.ActiveSeats, 1_000_000)]
    [InlineData(QuotaResourceTypes.Sites, 99)]
    [InlineData(QuotaResourceTypes.Spaces, 99)]
    public void EnforceLimit_NeverThrows(string resourceType, int count)
    {
        var act = () => _enforcer.EnforceLimit(resourceType, count);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(QuotaResourceTypes.ActiveSeats)]
    [InlineData(QuotaResourceTypes.Sites)]
    [InlineData(QuotaResourceTypes.Spaces)]
    [InlineData("unknown-resource")]
    public void GetLimit_AlwaysReturnsUnlimited(string resourceType)
    {
        _enforcer.GetLimit(resourceType).Should().Be(-1);
    }
}
