using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Api.Integrations.Keycloak;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;

namespace Orkyo.Foundation.Tests.Helpers;

public class EndpointHelpersTests
{
    [Fact]
    public async Task ExecuteAsync_ShouldReturnHandlerResult_WhenNoException()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => Task.FromResult<IResult>(Results.Ok(new { ok = true })),
            logger,
            "test operation");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        payload.GetProperty("ok").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFallThroughToProblem_ForUnhandledInvalidOperationException()
    {
        // InvalidOperationException is intentionally NOT mapped to a specific status.
        // Repositories must throw ConflictException for known business violations; an
        // InvalidOperationException reaching the handler is an unexpected programming
        // error that should surface as a 500 Problem response for investigation.
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new InvalidOperationException("unexpected state"),
            logger,
            "some operation");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapArgumentException_To400()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new ArgumentException("bad input"),
            logger,
            "validate");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        payload.GetProperty("code").GetString().Should().Be(nameof(ErrorCodes.ValidationError));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapKeycloakAdmin404_ToNotFoundContract()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new KeycloakAdminException("kc missing", StatusCodes.Status404NotFound),
            logger,
            "keycloak call");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound);
        payload.GetProperty("error").GetString().Should().Be("kc missing");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapKeycloakAdminUnknownStatus_ToKeycloakError()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new KeycloakAdminException("kc upstream", StatusCodes.Status418ImATeapot),
            logger,
            "keycloak call");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status418ImATeapot);
        payload.GetProperty("code").GetString().Should().Be("KEYCLOAK_ERROR");
    }

    [Fact]
    public async Task ExecuteAsyncOfT_ShouldReturnNotFound_WhenHandlerReturnsNull()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync<object>(
            () => Task.FromResult<object?>(null),
            logger,
            "load optional");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ExecuteAsyncWithValidator_ShouldReturnValidationProblem_WhenRequestInvalid()
    {
        var logger = Mock.Of<ILogger>();
        var validator = new DummyRequestValidator();

        var result = await EndpointHelpers.ExecuteAsync(
            new DummyRequest { Name = "" },
            validator,
            () => Task.FromResult<IResult>(Results.Ok()),
            logger,
            "validate request");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ExecuteAsyncWithValidatorAndResult_ShouldReturnOk_WhenValid()
    {
        var validator = new DummyRequestValidator();

        var result = await EndpointHelpers.ExecuteAsync<DummyRequest, object>(
            new DummyRequest { Name = "ok" },
            validator,
            () => Task.FromResult<object>(new { done = true }));

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        payload.GetProperty("done").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsyncWithValidatorAndResult_ShouldReturnValidationProblem_WhenInvalid()
    {
        var validator = new DummyRequestValidator();

        var result = await EndpointHelpers.ExecuteAsync<DummyRequest, object>(
            new DummyRequest { Name = "" },
            validator,
            () => Task.FromResult<object>(new { done = true }));

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ExecuteAsyncWithValidator_ShouldCallHandler_WhenRequestIsValid()
    {
        var logger = Mock.Of<ILogger>();
        var validator = new DummyRequestValidator();

        var result = await EndpointHelpers.ExecuteAsync(
            new DummyRequest { Name = "valid" },
            validator,
            () => Task.FromResult<IResult>(Results.Ok(new { success = true })),
            logger,
            "valid request");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsyncOfT_ShouldReturnOk_WhenHandlerReturnsValue()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync<object>(
            () => Task.FromResult<object?>(new { value = 42 }),
            logger,
            "load item");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        payload.GetProperty("value").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapKeyNotFoundException_To404()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new KeyNotFoundException("resource missing"),
            logger,
            "find resource");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapUnauthorizedAccessException_ToForbid()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new UnauthorizedAccessException(),
            logger,
            "guarded action");

        // Verify the result type without executing (ForbidHttpResult requires auth middleware)
        result.Should().BeOfType<ForbidHttpResult>();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnProblem_ForUnhandledException()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new InvalidTimeZoneException("unexpected"),
            logger,
            "crash");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnProblem_WhenLoggerIsNull()
    {
        // The 3-arg ExecuteAsync<TRequest, TResult> passes null logger to MapExceptionToResult
        var validator = new DummyRequestValidator();

        var result = await EndpointHelpers.ExecuteAsync<DummyRequest, object>(
            new DummyRequest { Name = "ok" },
            validator,
            () => throw new InvalidTimeZoneException("boom"));

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapPostgresException23505_ToConflict()
    {
        var logger = Mock.Of<ILogger>();
        var pgEx = (PostgresException)RuntimeHelpers.GetUninitializedObject(typeof(PostgresException));
        typeof(PostgresException)
            .GetField("<SqlState>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(pgEx, "23505");

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw pgEx,
            logger,
            "insert");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapKeycloakAdmin400_ToBadRequest()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new KeycloakAdminException("bad request", StatusCodes.Status400BadRequest),
            logger,
            "keycloak call");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        payload.GetProperty("code").GetString().Should().Be(nameof(ErrorCodes.ValidationError));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMapKeycloakAdmin409_ToConflict()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync(
            () => throw new KeycloakAdminException("conflict", StatusCodes.Status409Conflict),
            logger,
            "keycloak call");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        payload.GetProperty("code").GetString().Should().Be(ErrorCodes.Conflict);
    }

    [Fact]
    public async Task ExecuteAsyncOfT_ShouldMapException_WhenHandlerThrows()
    {
        var logger = Mock.Of<ILogger>();

        var result = await EndpointHelpers.ExecuteAsync<object>(
            () => throw new ArgumentException("bad arg"),
            logger,
            "load with error");

        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        var payload = await ReadJsonAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        payload.GetProperty("code").GetString().Should().Be(nameof(ErrorCodes.ValidationError));
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

    private sealed class DummyRequest
    {
        public string Name { get; init; } = string.Empty;
    }

    private sealed class DummyRequestValidator : AbstractValidator<DummyRequest>
    {
        public DummyRequestValidator()
        {
            RuleFor(x => x.Name).NotEmpty();
        }
    }
}
