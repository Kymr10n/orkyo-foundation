using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSlugAvailabilityScalarFlowTests
{
    [Fact]
    public void IsTaken_ShouldReturnFalse_WhenScalarIsNull()
    {
        TenantSlugAvailabilityScalarFlow.IsTaken(null).Should().BeFalse();
    }

    [Fact]
    public void IsTaken_ShouldReturnFalse_WhenScalarIsDbNull()
    {
        TenantSlugAvailabilityScalarFlow.IsTaken(DBNull.Value).Should().BeFalse();
    }

    [Fact]
    public void IsTaken_ShouldReturnTrue_WhenScalarHasTenantId()
    {
        TenantSlugAvailabilityScalarFlow.IsTaken(Guid.NewGuid()).Should().BeTrue();
    }
}
