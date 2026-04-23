using Api.Security;
using Api.Services;

namespace Orkyo.Foundation.Tests.Security;

public class SecurityContextImplTests
{
    [Fact]
    public void CurrentPrincipal_ShouldDefaultToAnonymousContext()
    {
        var principal = new CurrentPrincipal();

        principal.IsAuthenticated.Should().BeFalse();
        principal.UserId.Should().Be(Guid.Empty);
        principal.Email.Should().BeEmpty();
        principal.GetContext().Should().BeEquivalentTo(PrincipalContext.Anonymous);
    }

    [Fact]
    public void CurrentPrincipal_RequireUserId_ShouldThrow_WhenNotAuthenticated()
    {
        var principal = new CurrentPrincipal();

        var act = () => principal.RequireUserId();

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Authentication required");
    }

    [Fact]
    public void CurrentPrincipal_RequireExternalSubject_ShouldThrow_WhenMissing()
    {
        var principal = new CurrentPrincipal();

        var act = () => principal.RequireExternalSubject();

        act.Should().Throw<ArgumentException>()
            .WithMessage("User identity not found");
    }

    [Fact]
    public void CurrentPrincipal_ShouldExposeContextValues_AfterSetContext()
    {
        var userId = Guid.NewGuid();
        var principal = new CurrentPrincipal();

        principal.SetContext(new PrincipalContext
        {
            UserId = userId,
            Email = "admin@example.com",
            DisplayName = "Admin",
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = "kc-sub-1",
            IsSiteAdmin = true
        });

        principal.IsAuthenticated.Should().BeTrue();
        principal.UserId.Should().Be(userId);
        principal.IsSiteAdmin.Should().BeTrue();
        principal.RequireUserId().Should().Be(userId);
        principal.RequireExternalSubject().Should().Be("kc-sub-1");
    }

    [Fact]
    public void CurrentAuthorizationContext_ShouldThrow_WhenMembershipRequiredButMissing()
    {
        var context = new CurrentAuthorizationContext();

        var act = () => context.RequireMembership();

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Tenant membership required");
    }

    [Fact]
    public void CurrentAuthorizationContext_ShouldThrow_WhenRoleInsufficient()
    {
        var context = new CurrentAuthorizationContext();
        context.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Viewer
        });

        var act = () => context.RequireRole(TenantRole.Admin);

        act.Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Role Admin required, but user has Viewer");
    }

    [Fact]
    public void CurrentAuthorizationContext_ShouldAllow_WhenRoleMeetsRequirement()
    {
        var context = new CurrentAuthorizationContext();
        context.SetContext(new AuthorizationContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            Role = TenantRole.Admin
        });

        context.IsMember.Should().BeTrue();
        context.IsAdmin.Should().BeTrue();
        context.CanEdit.Should().BeTrue();
        context.CanView.Should().BeTrue();

        var act = () => context.RequireRole(TenantRole.Editor);
        act.Should().NotThrow();
    }
}
