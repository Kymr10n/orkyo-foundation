using Api.Constants;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantReactivationDecisionFlowTests
{
    [Fact]
    public void Evaluate_ShouldReturnNotMemberSnapshot_WhenLookupMissing()
    {
        var lookup = new TenantReactivationLookupSnapshot(false, null, null, null, null);

        var result = TenantReactivationDecisionFlow.Evaluate(lookup, Guid.NewGuid());

        result.IsMember.Should().BeFalse();
        result.Decision.Should().BeNull();
        result.Role.Should().BeNull();
        result.IsOwner.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_ForOwnerWhenSelfServiceSuspended()
    {
        var userId = Guid.NewGuid();
        var lookup = new TenantReactivationLookupSnapshot(
            true,
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            RoleConstants.Viewer,
            userId);

        var result = TenantReactivationDecisionFlow.Evaluate(lookup, userId);

        result.IsMember.Should().BeTrue();
        result.Decision.Should().Be(TenantReactivationDecision.Allowed);
        result.Role.Should().Be(RoleConstants.Viewer);
        result.IsOwner.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_ShouldReturnNotAuthorized_WhenNonOwnerNonAdmin()
    {
        var userId = Guid.NewGuid();
        var lookup = new TenantReactivationLookupSnapshot(
            true,
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            RoleConstants.Viewer,
            Guid.NewGuid());

        var result = TenantReactivationDecisionFlow.Evaluate(lookup, userId);

        result.IsMember.Should().BeTrue();
        result.Decision.Should().Be(TenantReactivationDecision.NotAuthorized);
        result.IsOwner.Should().BeFalse();
    }
}
