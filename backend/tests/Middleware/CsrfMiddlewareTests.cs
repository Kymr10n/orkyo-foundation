using System.Security.Claims;
using Api.Configuration;
using Api.Middleware;
using Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orkyo.Foundation.Tests.Middleware;

public class CsrfMiddlewareTests
{
    private readonly BffOptions _options = new();

    private CsrfMiddleware CreateMiddleware(RequestDelegate next) =>
        new CsrfMiddleware(next, new Mock<ILogger<CsrfMiddleware>>().Object);

    private DefaultHttpContext CreateContext(
        string method = "GET",
        string? authType = null,
        string? csrfCookie = null,
        string? csrfHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        if (authType is not null)
        {
            var identity = new ClaimsIdentity([], authType);
            context.User = new ClaimsPrincipal(identity);
        }

        if (csrfCookie is not null)
            context.Request.Headers.Cookie = $"{_options.CsrfCookieName}={csrfCookie}";

        if (csrfHeader is not null)
            context.Request.Headers[_options.CsrfHeaderName] = csrfHeader;

        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(_options));
        context.RequestServices = services.BuildServiceProvider();

        return context;
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task SafeMethods_PassThrough_WithBffAuth(string method)
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext(method, BffCookieAuthenticationHandler.SchemeName);

        await middleware.InvokeAsync(context, Options.Create(_options));

        called.Should().BeTrue();
    }

    [Fact]
    public async Task Post_WithJwtAuth_PassesThrough_WithoutCsrf()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext("POST", "Bearer");

        await middleware.InvokeAsync(context, Options.Create(_options));

        called.Should().BeTrue();
    }

    [Fact]
    public async Task Post_WithBffAuth_MatchingCsrf_PassesThrough()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext("POST", BffCookieAuthenticationHandler.SchemeName, "token-abc", "token-abc");

        await middleware.InvokeAsync(context, Options.Create(_options));

        called.Should().BeTrue();
    }

    [Fact]
    public async Task Post_WithBffAuth_MissingCsrf_Returns403()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext("POST", BffCookieAuthenticationHandler.SchemeName);

        await middleware.InvokeAsync(context, Options.Create(_options));

        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Post_WithBffAuth_WrongCsrf_Returns403()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext("POST", BffCookieAuthenticationHandler.SchemeName, "token-abc", "token-wrong");

        await middleware.InvokeAsync(context, Options.Create(_options));

        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Post_Unauthenticated_PassesThrough()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext("POST");

        await middleware.InvokeAsync(context, Options.Create(_options));

        called.Should().BeTrue();
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task MutatingMethods_WithBffAuth_NoCsrf_Returns403(string method)
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext(method, BffCookieAuthenticationHandler.SchemeName);

        await middleware.InvokeAsync(context, Options.Create(_options));

        called.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
