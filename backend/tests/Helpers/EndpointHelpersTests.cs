using System.Text.Json;
using Api.Helpers;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Orkyo.Foundation.Tests.Helpers;

public class EndpointHelpersTests
{
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
