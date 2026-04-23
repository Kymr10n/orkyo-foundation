using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

public class PrincipalContextTests
{
    [Fact]
    public void Anonymous_ShouldBeUnauthenticatedAndLocalProvider()
    {
        var principal = PrincipalContext.Anonymous;

        principal.UserId.Should().Be(Guid.Empty);
        principal.Email.Should().BeEmpty();
        principal.AuthProvider.Should().Be(AuthProvider.Local);
        principal.IsAuthenticated.Should().BeFalse();
        principal.IsSiteAdmin.Should().BeFalse();
        principal.ExternalSubject.Should().BeNull();
    }

    [Fact]
    public void IsAuthenticated_ShouldBeTrue_WhenUserIdIsNotEmpty()
    {
        var principal = new PrincipalContext
        {
            UserId = Guid.NewGuid(),
            Email = "owner@example.com",
            AuthProvider = AuthProvider.Keycloak,
            ExternalSubject = "kc-sub-123",
            IsSiteAdmin = true
        };

        principal.IsAuthenticated.Should().BeTrue();
        principal.IsSiteAdmin.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_ShouldBeFalse_WhenUserIdIsEmpty()
    {
        var principal = new PrincipalContext
        {
            UserId = Guid.Empty,
            Email = "ghost@example.com",
            AuthProvider = AuthProvider.Keycloak
        };

        principal.IsAuthenticated.Should().BeFalse();
    }
}
