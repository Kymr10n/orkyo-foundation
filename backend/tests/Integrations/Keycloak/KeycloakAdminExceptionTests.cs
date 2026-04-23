using Api.Integrations.Keycloak;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

public class KeycloakAdminExceptionTests
{
    [Fact]
    public void Defaults_StatusCode_To_502_BadGateway()
    {
        var ex = new KeycloakAdminException("upstream error");
        ex.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        ex.Message.Should().Be("upstream error");
    }

    [Fact]
    public void Preserves_Explicit_StatusCode()
    {
        var ex = new KeycloakAdminException("not found", StatusCodes.Status404NotFound);
        ex.StatusCode.Should().Be(404);
    }

    [Fact]
    public void Chains_Inner_Exception()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new KeycloakAdminException("wrapper", StatusCodes.Status500InternalServerError, inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}
