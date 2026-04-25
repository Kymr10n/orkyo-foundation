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

    [Fact]
    public void GetContext_WhenNoContextSet_ThrowsInvalidOperationException()
    {
        ((Action)(() => new CurrentAuthorizationContext().GetContext()))
            .Should().Throw<InvalidOperationException>().WithMessage("Authorization context not set");
    }

    [Fact]
    public void GetContext_WhenContextSet_ReturnsContext()
    {
        var ctx = new CurrentAuthorizationContext();
        var authCtx = new AuthorizationContext { TenantId = Guid.NewGuid(), TenantSlug = "acme", Role = TenantRole.Editor };
        ctx.SetContext(authCtx);
        ctx.GetContext().Should().BeSameAs(authCtx);
    }

    [Fact]
    public void RequireMembership_WhenNotMember_ThrowsUnauthorizedAccess()
    {
        ((Action)(() => new CurrentAuthorizationContext().RequireMembership()))
            .Should().Throw<UnauthorizedAccessException>().WithMessage("Tenant membership required");
    }

    [Fact]
    public void RequireMembership_WhenMember_DoesNotThrow()
    {
        var ctx = new CurrentAuthorizationContext();
        ctx.SetContext(new AuthorizationContext { TenantId = Guid.NewGuid(), TenantSlug = "acme", Role = TenantRole.Viewer });
        ((Action)(() => ctx.RequireMembership())).Should().NotThrow();
    }

    [Fact]
    public void RequireRole_WhenRoleSufficient_DoesNotThrow()
    {
        var ctx = new CurrentAuthorizationContext();
        ctx.SetContext(new AuthorizationContext { TenantId = Guid.NewGuid(), TenantSlug = "acme", Role = TenantRole.Admin });
        ((Action)(() => ctx.RequireRole(TenantRole.Editor))).Should().NotThrow();
    }

    [Fact]
    public void RequireRole_WhenRoleInsufficient_ThrowsUnauthorizedAccess()
    {
        var ctx = new CurrentAuthorizationContext();
        ctx.SetContext(new AuthorizationContext { TenantId = Guid.NewGuid(), TenantSlug = "acme", Role = TenantRole.Viewer });
        ((Action)(() => ctx.RequireRole(TenantRole.Editor)))
            .Should().Throw<UnauthorizedAccessException>().WithMessage("Role Editor required, but user has Viewer");
    }

    [Theory]
    [InlineData(TenantRole.Admin, true, true, true, true)]
    [InlineData(TenantRole.Editor, true, false, true, true)]
    [InlineData(TenantRole.Viewer, true, false, false, true)]
    [InlineData(TenantRole.None, false, false, false, false)]
    public void RoleFlags_MatchExpectedHierarchy(TenantRole role, bool isMember, bool isAdmin, bool canEdit, bool canView)
    {
        var ctx = new CurrentAuthorizationContext();
        ctx.SetContext(new AuthorizationContext { TenantId = Guid.NewGuid(), TenantSlug = "test", Role = role });

        ctx.IsMember.Should().Be(isMember);
        ctx.IsAdmin.Should().Be(isAdmin);
        ctx.CanEdit.Should().Be(canEdit);
        ctx.CanView.Should().Be(canView);
    }
}
