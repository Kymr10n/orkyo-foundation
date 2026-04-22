using Api.Constants;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantLeaveMembershipPolicyTests
{
    [Fact]
    public void Evaluate_ShouldReturnOwnerCannotLeave_WhenActorOwnsTenant()
    {
        var ownerId = Guid.NewGuid();

        var decision = TenantLeaveMembershipPolicy.Evaluate(ownerId, ownerId, RoleConstants.Admin, activeAdminCount: 2);

        decision.Should().Be(TenantLeaveMembershipDecision.OwnerCannotLeave);
    }

    [Fact]
    public void Evaluate_ShouldReturnNotMember_WhenRoleIsMissing()
    {
        var decision = TenantLeaveMembershipPolicy.Evaluate(Guid.NewGuid(), Guid.NewGuid(), actorRole: null, activeAdminCount: 2);

        decision.Should().Be(TenantLeaveMembershipDecision.NotMember);
    }

    [Fact]
    public void Evaluate_ShouldReturnLastAdminCannotLeave_WhenActorIsLastActiveAdmin()
    {
        var decision = TenantLeaveMembershipPolicy.Evaluate(Guid.NewGuid(), Guid.NewGuid(), RoleConstants.Admin, activeAdminCount: 1);

        decision.Should().Be(TenantLeaveMembershipDecision.LastAdminCannotLeave);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_WhenActorIsAdminButNotLast()
    {
        var decision = TenantLeaveMembershipPolicy.Evaluate(Guid.NewGuid(), Guid.NewGuid(), RoleConstants.Admin, activeAdminCount: 2);

        decision.Should().Be(TenantLeaveMembershipDecision.Allowed);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_WhenActorIsNonAdminMember()
    {
        var decision = TenantLeaveMembershipPolicy.Evaluate(Guid.NewGuid(), Guid.NewGuid(), RoleConstants.Editor, activeAdminCount: 1);

        decision.Should().Be(TenantLeaveMembershipDecision.Allowed);
    }
}
