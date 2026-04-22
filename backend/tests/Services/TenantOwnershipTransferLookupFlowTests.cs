using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnershipTransferLookupFlowTests
{
    [Fact]
    public void FromSnapshots_ShouldComposeNotFoundAndNoMembership()
    {
        var ownerStatus = new TenantOwnerStatusSnapshot(null, null, false);
        var membership = new TenantMembershipRoleStatusSnapshot(false, null, null);

        var result = TenantOwnershipTransferLookupFlow.FromSnapshots(ownerStatus, membership);

        result.TenantFound.Should().BeFalse();
        result.OwnerUserId.Should().BeNull();
        result.TenantStatus.Should().BeNull();
        result.NewOwnerMembershipFound.Should().BeFalse();
        result.NewOwnerRole.Should().BeNull();
        result.NewOwnerMembershipStatus.Should().BeNull();
    }

    [Fact]
    public void FromSnapshots_ShouldComposeFoundValues()
    {
        var ownerId = Guid.NewGuid();
        var ownerStatus = new TenantOwnerStatusSnapshot(ownerId, "active", true);
        var membership = new TenantMembershipRoleStatusSnapshot(true, "admin", "active");

        var result = TenantOwnershipTransferLookupFlow.FromSnapshots(ownerStatus, membership);

        result.TenantFound.Should().BeTrue();
        result.OwnerUserId.Should().Be(ownerId);
        result.TenantStatus.Should().Be("active");
        result.NewOwnerMembershipFound.Should().BeTrue();
        result.NewOwnerRole.Should().Be("admin");
        result.NewOwnerMembershipStatus.Should().Be("active");
    }
}
