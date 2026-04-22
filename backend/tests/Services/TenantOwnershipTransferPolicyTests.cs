using Api.Constants;
using Api.Services;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnershipTransferPolicyTests
{
    [Fact]
    public void Evaluate_ShouldReturnTenantNotFound_WhenTenantMissing()
    {
        var decision = TenantOwnershipTransferPolicy.Evaluate(
            tenantFound: false,
            ownerUserId: null,
            currentOwnerId: Guid.NewGuid(),
            isSiteAdmin: false,
            tenantStatus: null,
            newOwnerMembershipFound: false,
            newOwnerRole: null,
            newOwnerMembershipStatus: null);

        decision.Should().Be(TenantOwnershipTransferDecision.TenantNotFound);
    }

    [Fact]
    public void Evaluate_ShouldReturnNotCurrentOwner_WhenCallerIsNotOwnerAndNotSiteAdmin()
    {
        var decision = TenantOwnershipTransferPolicy.Evaluate(
            tenantFound: true,
            ownerUserId: Guid.NewGuid(),
            currentOwnerId: Guid.NewGuid(),
            isSiteAdmin: false,
            tenantStatus: TenantStatusConstants.Active,
            newOwnerMembershipFound: true,
            newOwnerRole: RoleConstants.Admin,
            newOwnerMembershipStatus: "active");

        decision.Should().Be(TenantOwnershipTransferDecision.NotCurrentOwner);
    }

    [Fact]
    public void Evaluate_ShouldReturnTenantDeleting_WhenTenantIsDeleting()
    {
        var ownerId = Guid.NewGuid();
        var decision = TenantOwnershipTransferPolicy.Evaluate(
            tenantFound: true,
            ownerUserId: ownerId,
            currentOwnerId: ownerId,
            isSiteAdmin: false,
            tenantStatus: TenantStatusConstants.Deleting,
            newOwnerMembershipFound: true,
            newOwnerRole: RoleConstants.Admin,
            newOwnerMembershipStatus: "active");

        decision.Should().Be(TenantOwnershipTransferDecision.TenantDeleting);
    }

    [Fact]
    public void Evaluate_ShouldReturnNewOwnerNotMember_WhenMembershipMissing()
    {
        var ownerId = Guid.NewGuid();
        var decision = TenantOwnershipTransferPolicy.Evaluate(
            tenantFound: true,
            ownerUserId: ownerId,
            currentOwnerId: ownerId,
            isSiteAdmin: false,
            tenantStatus: TenantStatusConstants.Active,
            newOwnerMembershipFound: false,
            newOwnerRole: null,
            newOwnerMembershipStatus: null);

        decision.Should().Be(TenantOwnershipTransferDecision.NewOwnerNotMember);
    }

    [Fact]
    public void Evaluate_ShouldReturnNewOwnerNotAdmin_WhenRoleIsNotAdmin()
    {
        var ownerId = Guid.NewGuid();
        var decision = TenantOwnershipTransferPolicy.Evaluate(
            tenantFound: true,
            ownerUserId: ownerId,
            currentOwnerId: ownerId,
            isSiteAdmin: false,
            tenantStatus: TenantStatusConstants.Active,
            newOwnerMembershipFound: true,
            newOwnerRole: RoleConstants.Viewer,
            newOwnerMembershipStatus: "active");

        decision.Should().Be(TenantOwnershipTransferDecision.NewOwnerNotAdmin);
    }

    [Fact]
    public void Evaluate_ShouldReturnNewOwnerMembershipNotActive_WhenStatusIsNotActive()
    {
        var ownerId = Guid.NewGuid();
        var decision = TenantOwnershipTransferPolicy.Evaluate(
            tenantFound: true,
            ownerUserId: ownerId,
            currentOwnerId: ownerId,
            isSiteAdmin: false,
            tenantStatus: TenantStatusConstants.Active,
            newOwnerMembershipFound: true,
            newOwnerRole: RoleConstants.Admin,
            newOwnerMembershipStatus: "pending");

        decision.Should().Be(TenantOwnershipTransferDecision.NewOwnerMembershipNotActive);
    }

    [Fact]
    public void Evaluate_ShouldReturnAllowed_WhenAllConditionsPass()
    {
        var ownerId = Guid.NewGuid();
        var decision = TenantOwnershipTransferPolicy.Evaluate(
            tenantFound: true,
            ownerUserId: ownerId,
            currentOwnerId: ownerId,
            isSiteAdmin: false,
            tenantStatus: TenantStatusConstants.Active,
            newOwnerMembershipFound: true,
            newOwnerRole: RoleConstants.Admin,
            newOwnerMembershipStatus: "active");

        decision.Should().Be(TenantOwnershipTransferDecision.Allowed);
    }
}
