using Api.Constants;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantReactivationPolicyTests
{
    [Fact]
    public void Evaluate_ShouldReturnNotSuspended_WhenStatusIsNotSuspended()
    {
        var decision = TenantReactivationPolicy.Evaluate(
            TenantStatusConstants.Active,
            SuspensionReasonConstants.Inactivity,
            RoleConstants.Admin,
            isOwner: false);

        decision.Should().Be(TenantReactivationDecision.NotSuspended);
    }

    [Fact]
    public void Evaluate_ShouldReturnNotSelfService_WhenReasonIsNotEligible()
    {
        var decision = TenantReactivationPolicy.Evaluate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.ManualAdmin,
            RoleConstants.Admin,
            isOwner: true);

        decision.Should().Be(TenantReactivationDecision.NotSelfService);
    }

    [Fact]
    public void Evaluate_ShouldReturnNotAuthorized_WhenNotOwnerAndNotAdmin()
    {
        var decision = TenantReactivationPolicy.Evaluate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            RoleConstants.Viewer,
            isOwner: false);

        decision.Should().Be(TenantReactivationDecision.NotAuthorized);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_ForOwner()
    {
        var decision = TenantReactivationPolicy.Evaluate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            role: null,
            isOwner: true);

        decision.Should().Be(TenantReactivationDecision.Allowed);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_ForAdminRole_CaseInsensitive()
    {
        var decision = TenantReactivationPolicy.Evaluate(
            TenantStatusConstants.Suspended,
            SuspensionReasonConstants.Inactivity,
            role: "AdMiN",
            isOwner: false);

        decision.Should().Be(TenantReactivationDecision.Allowed);
    }
}
