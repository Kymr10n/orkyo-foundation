using Api.Constants;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class SuspensionPolicyTests
{
    [Fact]
    public void IsSelfServiceReason_ReturnsTrue_ForInactivity_CaseInsensitive()
    {
        SuspensionPolicy.IsSelfServiceReason("InActivity").Should().BeTrue();
    }

    [Fact]
    public void IsSelfServiceReason_ReturnsFalse_ForNonSelfServiceReason()
    {
        SuspensionPolicy.IsSelfServiceReason(SuspensionReasonConstants.ManualAdmin).Should().BeFalse();
    }

    [Fact]
    public void CanReactivate_ReturnsFalse_WhenStatusIsNotSuspended()
    {
        var canReactivate = SuspensionPolicy.CanReactivate(
            TenantStatusConstants.Active,
            SuspensionReasonConstants.Inactivity,
            RoleConstants.Admin,
            isOwner: false);

        canReactivate.Should().BeFalse();
    }

    [Fact]
    public void CanReactivate_ReturnsFalse_WhenReasonIsNotSelfService()
    {
        var canReactivate = SuspensionPolicy.CanReactivate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.ManualAdmin,
            RoleConstants.Admin,
            isOwner: true);

        canReactivate.Should().BeFalse();
    }

    [Fact]
    public void CanReactivate_ReturnsTrue_ForOwner_WhenSelfServiceSuspended()
    {
        var canReactivate = SuspensionPolicy.CanReactivate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            role: null,
            isOwner: true);

        canReactivate.Should().BeTrue();
    }

    [Fact]
    public void CanReactivate_ReturnsTrue_ForAdminRole_WhenSelfServiceSuspended()
    {
        var canReactivate = SuspensionPolicy.CanReactivate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            role: "AdMiN",
            isOwner: false);

        canReactivate.Should().BeTrue();
    }

    [Fact]
    public void CanReactivate_ReturnsFalse_ForViewer_WhenSelfServiceSuspended()
    {
        var canReactivate = SuspensionPolicy.CanReactivate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            role: RoleConstants.Viewer,
            isOwner: false);

        canReactivate.Should().BeFalse();
    }
}
