using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

public class CurrentPrincipalTests
{
    [Fact]
    public void RequireUserId_WhenAuthenticated_ReturnsUserId()
    {
        var principal = new CurrentPrincipal();
        var userId = Guid.NewGuid();
        principal.SetContext(new PrincipalContext
        {
            UserId = userId, Email = "user@example.com",
            AuthProvider = AuthProvider.Keycloak, ExternalSubject = "kc-sub-123"
        });

        principal.RequireUserId().Should().Be(userId);
    }

    [Fact]
    public void RequireUserId_WhenNotAuthenticated_ThrowsUnauthorizedAccess()
    {
        var principal = new CurrentPrincipal();
        ((Action)(() => principal.RequireUserId())).Should().Throw<UnauthorizedAccessException>()
            .WithMessage("Authentication required");
    }

    [Fact]
    public void RequireUserId_WhenAnonymousContext_ThrowsUnauthorizedAccess()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(PrincipalContext.Anonymous);
        ((Action)(() => principal.RequireUserId())).Should().Throw<UnauthorizedAccessException>();
    }

    [Fact]
    public void RequireExternalSubject_WithValidSubject_ReturnsSubject()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(), Email = "user@example.com",
            AuthProvider = AuthProvider.Keycloak, ExternalSubject = "kc-sub-456"
        });

        principal.RequireExternalSubject().Should().Be("kc-sub-456");
    }

    [Fact]
    public void RequireExternalSubject_WithNullSubject_ThrowsArgumentException()
    {
        var principal = new CurrentPrincipal();
        principal.SetContext(new PrincipalContext
        {
            UserId = Guid.NewGuid(), Email = "user@example.com",
            AuthProvider = AuthProvider.Local, ExternalSubject = null
        });

        ((Action)(() => principal.RequireExternalSubject())).Should().Throw<ArgumentException>()
            .WithMessage("User identity not found");
    }

    [Fact]
    public void RequireExternalSubject_WithNoContext_ThrowsArgumentException()
    {
        var principal = new CurrentPrincipal();
        ((Action)(() => principal.RequireExternalSubject())).Should().Throw<ArgumentException>();
    }
}
