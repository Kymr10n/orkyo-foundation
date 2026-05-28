using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Orkyo.Foundation.Tests.Helpers;

public class AppExceptionHandlerTests
{
    private static AppExceptionHandler Handler => new();

    [Fact]
    public async Task FeatureNotAvailableException_Maps_To403()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new FeatureNotAvailableException("Auto-Schedule", "not enabled"), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("error").GetString().Should().Contain("Auto-Schedule");
    }

    [Fact]
    public async Task QuotaExceededException_Maps_To403_WithStructuredPayload()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new QuotaExceededException("spaces", 15), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(Api.Constants.ApiErrorCodes.QuotaExceeded);
        body.GetProperty("resourceType").GetString().Should().Be("spaces");
        body.GetProperty("limit").GetInt32().Should().Be(15);
    }

    [Fact]
    public async Task NotFoundException_Maps_To404()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new NotFoundException("Space", Guid.NewGuid()), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ConflictException_Maps_To409()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new ConflictException("already exists"), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task KeyNotFoundException_Maps_To404()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new KeyNotFoundException("resource missing"), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task CapabilityNotApplicableException_Maps_To400()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new CapabilityNotApplicableException(Guid.NewGuid(), Guid.NewGuid(), "bad capability"), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ArgumentException_Maps_To400()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new ArgumentException("bad input"), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(nameof(ErrorCodes.ValidationError));
    }

    [Fact]
    public async Task UnauthorizedAccessException_Maps_To403()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new UnauthorizedAccessException(), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task KeycloakAdminException404_Maps_ToNotFoundContract()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new KeycloakAdminException("kc missing", StatusCodes.Status404NotFound), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound);
        body.GetProperty("error").GetString().Should().Be("kc missing");
    }

    [Fact]
    public async Task KeycloakAdminException400_Maps_ToBadRequest()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new KeycloakAdminException("bad request", StatusCodes.Status400BadRequest), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(nameof(ErrorCodes.ValidationError));
    }

    [Fact]
    public async Task KeycloakAdminException409_Maps_ToConflict()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new KeycloakAdminException("conflict", StatusCodes.Status409Conflict), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task KeycloakAdminExceptionUnknownStatus_Maps_ToKeycloakError()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new KeycloakAdminException("kc upstream", StatusCodes.Status418ImATeapot), default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status418ImATeapot);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(KeycloakAdminExceptionMapper.KeycloakErrorCode);
    }

    [Fact]
    public async Task PostgresException23505_Maps_ToConflict()
    {
        var pgEx = (PostgresException)RuntimeHelpers.GetUninitializedObject(typeof(PostgresException));
        typeof(PostgresException)
            .GetField("<SqlState>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(pgEx, "23505");

        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, pgEx, default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var body = await ReadJsonAsync(ctx);
        body.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task BadHttpRequestException_Maps_To400()
    {
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(
            ctx,
            new Microsoft.AspNetCore.Http.BadHttpRequestException("Required parameter \"string name\" was not provided from query string."),
            default);

        handled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UnhandledException_ReturnsFalse_WithoutWritingResponse()
    {
        // InvalidOperationException is an unexpected programming error — let the framework log and 500 it.
        var ctx = CreateHttpContext();
        var handled = await Handler.TryHandleAsync(ctx, new InvalidOperationException("unexpected"), default);

        handled.Should().BeFalse();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK); // nothing written
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
