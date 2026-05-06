using Api.Security;

namespace Orkyo.Foundation.Tests.Security;

public class AuthorizationExceptionTests
{
    // ── Constructor ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new AuthorizationException("Access denied");
        ex.Message.Should().Be("Access denied");
    }

    [Fact]
    public void Constructor_WithoutExplicitStatusCode_DefaultsTo403()
    {
        var ex = new AuthorizationException("denied");
        ex.StatusCode.Should().Be(403);
    }

    [Fact]
    public void Constructor_WithCustomStatusCode_SetsStatusCode()
    {
        var ex = new AuthorizationException("not found", 404);
        ex.StatusCode.Should().Be(404);
        ex.Message.Should().Be("not found");
    }

    [Fact]
    public void Constructor_IsException()
    {
        var ex = new AuthorizationException("error");
        ex.Should().BeAssignableTo<Exception>();
    }

    // ── Factory methods ────────────────────────────────────────────────────

    [Fact]
    public void NotAuthenticated_Returns401WithExpectedMessage()
    {
        var ex = AuthorizationException.NotAuthenticated();
        ex.StatusCode.Should().Be(401);
        ex.Message.Should().Be("Authentication required");
    }

    [Fact]
    public void NotMember_Returns403WithExpectedMessage()
    {
        var ex = AuthorizationException.NotMember();
        ex.StatusCode.Should().Be(403);
        ex.Message.Should().Be("You are not a member of this tenant");
    }

    [Fact]
    public void InsufficientRole_Returns403WithFormattedMessage()
    {
        var ex = AuthorizationException.InsufficientRole(TenantRole.Admin, TenantRole.Viewer);
        ex.StatusCode.Should().Be(403);
        ex.Message.Should().Contain("Admin").And.Contain("Viewer");
    }

    [Fact]
    public void TenantNotFound_Returns404WithExpectedMessage()
    {
        var ex = AuthorizationException.TenantNotFound();
        ex.StatusCode.Should().Be(404);
        ex.Message.Should().Be("Tenant not found");
    }
}
