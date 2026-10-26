using System.Text.Json;
using Api.Constants;
using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

public class KeycloakAdminExceptionMapperTests
{
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, ApiErrorCodes.ValidationError)]
    [InlineData(StatusCodes.Status404NotFound, ApiErrorCodes.NotFound)]
    [InlineData(StatusCodes.Status409Conflict, ApiErrorCodes.Conflict)]
    public async Task Map_AKnownStatus_KeepsTheStatusAndMapsTheCode(int keycloakStatus, string code)
    {
        var ex = new KeycloakAdminException("from keycloak", keycloakStatus);

        var (status, payload) = await ExecuteAsync(KeycloakAdminExceptionMapper.Map(ex));

        status.Should().Be(keycloakStatus);
        payload.GetProperty("code").GetString().Should().Be(code);
        payload.GetProperty("detail").GetString().Should().Be("from keycloak");
    }

    [Fact]
    public async Task Map_UnknownStatus_PreservesStatus_AndUsesKeycloakErrorCode()
    {
        var ex = new KeycloakAdminException("kc upstream", StatusCodes.Status418ImATeapot);

        var (status, payload) = await ExecuteAsync(KeycloakAdminExceptionMapper.Map(ex));

        status.Should().Be(StatusCodes.Status418ImATeapot);
        payload.GetProperty("code").GetString().Should().Be("KEYCLOAK_ERROR");
        payload.GetProperty("detail").GetString().Should().Be("kc upstream");
    }

    [Fact]
    public async Task Map_Default502_PreservesStatus_AndUsesKeycloakErrorCode()
    {
        // Default ctor status code is 502 Bad Gateway.
        var ex = new KeycloakAdminException("upstream broken");

        var (status, payload) = await ExecuteAsync(KeycloakAdminExceptionMapper.Map(ex));

        status.Should().Be(StatusCodes.Status502BadGateway);
        payload.GetProperty("code").GetString().Should().Be("KEYCLOAK_ERROR");
    }

    [Fact]
    public void Map_Null_Throws()
    {
        var act = () => KeycloakAdminExceptionMapper.Map(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void KeycloakErrorCode_WireConstant_IsLocked()
    {
        // Drift guard: clients (frontend, dashboards) switch on this exact string.
        KeycloakAdminExceptionMapper.KeycloakErrorCode.Should().Be("KEYCLOAK_ERROR");
    }

    private static async Task<(int Status, JsonElement Payload)> ExecuteAsync(IResult result)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };

        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, json.RootElement.Clone());
    }
}
