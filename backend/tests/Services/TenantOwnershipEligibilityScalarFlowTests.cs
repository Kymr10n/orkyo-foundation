using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnershipEligibilityScalarFlowTests
{
    [Fact]
    public void CanCreateTenant_ShouldReturnTrue_WhenScalarIsNull()
    {
        TenantOwnershipEligibilityScalarFlow.CanCreateTenant(null).Should().BeTrue();
    }

    [Fact]
    public void CanCreateTenant_ShouldReturnTrue_WhenCountIsZero()
    {
        TenantOwnershipEligibilityScalarFlow.CanCreateTenant(0L).Should().BeTrue();
    }

    [Fact]
    public void CanCreateTenant_ShouldReturnFalse_WhenCountIsPositive()
    {
        TenantOwnershipEligibilityScalarFlow.CanCreateTenant(1L).Should().BeFalse();
    }

    [Fact]
    public void CanCreateTenant_ShouldHandleIntScalars()
    {
        TenantOwnershipEligibilityScalarFlow.CanCreateTenant(2).Should().BeFalse();
    }
}
