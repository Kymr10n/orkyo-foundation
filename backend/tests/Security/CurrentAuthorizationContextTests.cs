using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

public class CurrentAuthorizationContextTests
{
    [Fact]
    public void IsMember_WhenNoContextSet_ReturnsFalse() =>
        new CurrentAuthorizationContext().IsMember.Should().BeFalse();

    [Fact]
    public void Role_WhenNoContextSet_ReturnsNone() =>
        new CurrentAuthorizationContext().Role.Should().Be(TenantRole.None);

    [Theory]
    [InlineData(TenantRole.Admin, true, true, true)]
    [InlineData(TenantRole.Editor, true, false, true)]
    [InlineData(TenantRole.Viewer, true, false, false)]
    [InlineData(TenantRole.None, false, false, false)]
    public void RoleFlags_MatchExpectedHierarchy(TenantRole role, bool isMember, bool isAdmin, bool canEdit)
    {
        var ctx = new CurrentAuthorizationContext();
        ctx.SetContext(new AuthorizationContext { TenantId = Guid.NewGuid(), TenantSlug = "test", Role = role });

        ctx.IsMember.Should().Be(isMember);
        ctx.IsAdmin.Should().Be(isAdmin);
        ctx.CanEdit.Should().Be(canEdit);
    }
}
