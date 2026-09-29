using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

public class AuthorizationContextTests
{
    [Theory]
    [InlineData(TenantRole.None, false, false)]
    [InlineData(TenantRole.Viewer, true, false)]
    [InlineData(TenantRole.Editor, true, false)]
    [InlineData(TenantRole.Admin, true, true)]
    public void RoleFlags_ShouldMatchRoleHierarchy(
        TenantRole role,
        bool isMember,
        bool isAdmin)
    {
        var context = new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "tenant",
            Role = role
        };

        context.IsMember.Should().Be(isMember);
        context.IsAdmin.Should().Be(isAdmin);
        context.CanEdit.Should().Be(role >= TenantRole.Editor);
    }
}
