using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using static Orkyo.Foundation.Tests.Helpers.HttpContextTestHelpers;

namespace Orkyo.Foundation.Tests.Helpers;

public class ErrorResponsesTests
{
    [Fact]
    public async Task Unauthorized_ShouldReturn401_WithDefaultSessionExpiredCode()
    {
        var context = CreateHttpContext();

        await ErrorResponses.Unauthorized().ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        payload.GetProperty("code").GetString().Should().Be(ApiErrorCodes.SessionExpired);
        payload.GetProperty("detail").GetString().Should().Be("Not authenticated");
    }

    [Fact]
    public async Task Forbidden_ShouldReturn403_WithProvidedCodeAndReturnTo()
    {
        var context = CreateHttpContext();

        await ErrorResponses.Forbidden(
            code: ApiErrorCodes.BreakGlassExpired,
            message: "Break-glass ended",
            returnTo: "/admin").ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        payload.GetProperty("code").GetString().Should().Be(ApiErrorCodes.BreakGlassExpired);
        payload.GetProperty("detail").GetString().Should().Be("Break-glass ended");
        payload.GetProperty("returnTo").GetString().Should().Be("/admin");
    }

    [Fact]
    public async Task NotFound_ShouldReturn404_WithResourceTypeAndMessage()
    {
        var context = CreateHttpContext();
        var id = TestConstants.UserId;

        await ErrorResponses.NotFound("Tenant", id).ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        payload.GetProperty("code").GetString().Should().Be(ApiErrorCodes.NotFound);
        payload.GetProperty("resourceType").GetString().Should().Be("Tenant");
        payload.GetProperty("detail").GetString().Should().Contain("Tenant with ID");
    }

    [Fact]
    public async Task BadRequest_ShouldReturn400_WithDefaultValidationErrorCode()
    {
        var context = CreateHttpContext();

        await ErrorResponses.BadRequest("Invalid payload").ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        payload.GetProperty("code").GetString().Should().Be(nameof(ApiErrorCodes.ValidationError));
        payload.GetProperty("detail").GetString().Should().Be("Invalid payload");
    }

    [Fact]
    public async Task Conflict_ShouldReturn409_WithConflictCode()
    {
        var context = CreateHttpContext();

        await ErrorResponses.Conflict("Already exists").ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        payload.GetProperty("code").GetString().Should().Be(ApiErrorCodes.Conflict);
        payload.GetProperty("detail").GetString().Should().Be("Already exists");
    }
}
