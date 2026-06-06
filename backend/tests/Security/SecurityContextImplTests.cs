using Api.Models;
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

    // ── CurrentPrincipal.DisplayName ───────────────────────────────────────

    [Fact]
    public void CurrentPrincipal_DisplayName_DefaultsToNull()
    {
        var principal = new CurrentPrincipal();
        principal.DisplayName.Should().BeNull();
    }

    [Fact]
    public void CurrentPrincipal_DisplayName_ReturnsValueAfterSetContext()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "user@test.example",
            AuthProvider = AuthProvider.Keycloak,
            DisplayName = "Alice"
        });

        principal.DisplayName.Should().Be("Alice");
    }

    // ── CurrentTenant.TenantDbConnectionString ─────────────────────────────

    [Fact]
    public void CurrentTenant_TenantDbConnectionString_DefaultsToEmpty()
    {
        var tenant = new CurrentTenant();
        tenant.TenantDbConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void CurrentTenant_TenantDbConnectionString_ReturnsValueAfterSetContext()
    {
        var tenant = new CurrentTenant();
        tenant.SetContext(new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            TenantDbConnectionString = "Host=pg;Database=acme",
            Status = "active"
        });

        tenant.TenantDbConnectionString.Should().Be("Host=pg;Database=acme");
    }

    [Fact]
    public void CurrentTenant_TenantSlug_ReturnsValueAfterSetContext()
    {
        var tenant = new CurrentTenant();
        tenant.SetContext(new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "my-org",
            TenantDbConnectionString = "Host=pg",
            Status = "active"
        });

        tenant.TenantSlug.Should().Be("my-org");
    }
}
