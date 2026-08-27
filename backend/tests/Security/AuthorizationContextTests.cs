using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

public class AuthorizationContextTests
{
    [Theory]
    [InlineData(TenantRole.None, false, false, false)]
    [InlineData(TenantRole.Viewer, true, false, true)]
    [InlineData(TenantRole.Editor, true, false, true)]
    [InlineData(TenantRole.Admin, true, true, true)]
    public void RoleFlags_ShouldMatchRoleHierarchy(
        TenantRole role,
        bool isMember,
        bool isAdmin,
        bool canView)
    {
        var context = new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "tenant",
            Role = role
        };

        context.IsMember.Should().Be(isMember);
        context.IsAdmin.Should().Be(isAdmin);
        context.CanView.Should().Be(canView);
        context.CanEdit.Should().Be(role >= TenantRole.Editor);
    }

    [Fact]
    public void RoleString_ShouldBeLowercaseInvariant()
    {
        var context = new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "tenant",
            Role = TenantRole.Admin
        };

        context.RoleString.Should().Be("admin");
    }
}
