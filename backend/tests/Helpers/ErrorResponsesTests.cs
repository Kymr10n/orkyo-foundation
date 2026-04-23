using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        payload.GetProperty("error").GetString().Should().Be("Not authenticated");
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
        payload.GetProperty("error").GetString().Should().Be("Break-glass ended");
        payload.GetProperty("returnTo").GetString().Should().Be("/admin");
    }

    [Fact]
    public async Task NotFound_ShouldReturn404_WithResourceTypeAndMessage()
    {
        var context = CreateHttpContext();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await ErrorResponses.NotFound("Tenant", id).ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound);
        payload.GetProperty("resourceType").GetString().Should().Be("Tenant");
        payload.GetProperty("error").GetString().Should().Contain("Tenant with ID");
    }

    [Fact]
    public async Task BadRequest_ShouldReturn400_WithDefaultValidationErrorCode()
    {
        var context = CreateHttpContext();

        await ErrorResponses.BadRequest("Invalid payload").ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        payload.GetProperty("code").GetString().Should().Be(nameof(ErrorCodes.ValidationError));
        payload.GetProperty("error").GetString().Should().Be("Invalid payload");
    }

    [Fact]
    public async Task Conflict_ShouldReturn409_WithConflictCode()
    {
        var context = CreateHttpContext();

        await ErrorResponses.Conflict("Already exists").ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
        payload.GetProperty("error").GetString().Should().Be("Already exists");
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = new MemoryStream() }
        };
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        return json.RootElement.Clone();
    }
}
