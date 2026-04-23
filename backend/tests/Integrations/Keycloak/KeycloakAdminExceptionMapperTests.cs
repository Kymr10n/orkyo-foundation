using System.Text.Json;
using Api.Constants;
using Api.Integrations.Keycloak;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

public class KeycloakAdminExceptionMapperTests
{
    [Fact]
    public async Task Map_Status400_ReturnsBadRequest_WithValidationCode()
    {
        var ex = new KeycloakAdminException("bad input", StatusCodes.Status400BadRequest);

        var (status, payload) = await ExecuteAsync(KeycloakAdminExceptionMapper.Map(ex));

        status.Should().Be(StatusCodes.Status400BadRequest);
        payload.GetProperty("code").GetString().Should().Be(nameof(ErrorCodes.ValidationError));
        payload.GetProperty("error").GetString().Should().Be("bad input");
    }

    [Fact]
    public async Task Map_Status404_ReturnsNotFound_WithNotFoundCode()
    {
        var ex = new KeycloakAdminException("kc missing", StatusCodes.Status404NotFound);

        var (status, payload) = await ExecuteAsync(KeycloakAdminExceptionMapper.Map(ex));

        status.Should().Be(StatusCodes.Status404NotFound);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound);
        payload.GetProperty("error").GetString().Should().Be("kc missing");
    }

    [Fact]
    public async Task Map_Status409_ReturnsConflict_WithConflictCode()
    {
        var ex = new KeycloakAdminException("conflict", StatusCodes.Status409Conflict);

        var (status, payload) = await ExecuteAsync(KeycloakAdminExceptionMapper.Map(ex));

        status.Should().Be(StatusCodes.Status409Conflict);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
        payload.GetProperty("error").GetString().Should().Be("conflict");
    }

    [Fact]
    public async Task Map_UnknownStatus_PreservesStatus_AndUsesKeycloakErrorCode()
    {
        var ex = new KeycloakAdminException("kc upstream", StatusCodes.Status418ImATeapot);

        var (status, payload) = await ExecuteAsync(KeycloakAdminExceptionMapper.Map(ex));

        status.Should().Be(StatusCodes.Status418ImATeapot);
        payload.GetProperty("code").GetString().Should().Be("KEYCLOAK_ERROR");
        payload.GetProperty("error").GetString().Should().Be("kc upstream");
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
