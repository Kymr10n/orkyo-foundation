using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnerAccessPolicyTests
{
    [Fact]
    public void Evaluate_ShouldReturnTenantNotFound_WhenTenantMissing()
    {
        var decision = TenantOwnerAccessPolicy.Evaluate(false, null, Guid.NewGuid(), false);

        decision.Should().Be(TenantOwnerAccessDecision.TenantNotFound);
    }

    [Fact]
    public void Evaluate_ShouldReturnNotOwner_WhenActorNotOwnerAndNotSiteAdmin()
    {
        var decision = TenantOwnerAccessPolicy.Evaluate(true, Guid.NewGuid(), Guid.NewGuid(), false);

        decision.Should().Be(TenantOwnerAccessDecision.NotOwner);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_WhenActorIsOwner()
    {
        var actor = Guid.NewGuid();
        var decision = TenantOwnerAccessPolicy.Evaluate(true, actor, actor, false);

        decision.Should().Be(TenantOwnerAccessDecision.Allowed);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_WhenSiteAdminOverridesOwnership()
    {
        var decision = TenantOwnerAccessPolicy.Evaluate(true, Guid.NewGuid(), Guid.NewGuid(), true);

        decision.Should().Be(TenantOwnerAccessDecision.Allowed);
    }

    [Fact]
    public void IsDeleting_ShouldBeCaseInsensitive()
    {
        TenantOwnerAccessPolicy.IsDeleting("DeLeTiNg").Should().BeTrue();
    }

    [Fact]
    public void IsDeleting_ShouldReturnFalse_ForOtherStatuses()
    {
        TenantOwnerAccessPolicy.IsDeleting(TenantStatusConstants.Active).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, TenantOwnerAccessDecision.NotOwner)]
    [InlineData(true, TenantOwnerAccessDecision.Allowed)]
    public void Evaluate_AnOwnerlessTenant_IsOnlyASiteAdmins(bool isSiteAdmin, TenantOwnerAccessDecision expected)
    {
        var decision = TenantOwnerAccessPolicy.Evaluate(true, ownerUserId: null, Guid.NewGuid(), isSiteAdmin);

        decision.Should().Be(expected);
    }
}
