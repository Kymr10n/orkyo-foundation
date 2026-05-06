using Api.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Orkyo.Foundation.Tests.Middleware;

public class SecurityHeadersMiddlewareTests
{
    private static DefaultHttpContext CreateContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static SecurityHeadersMiddleware CreateMiddleware(bool isProduction)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName)
           .Returns(isProduction ? "Production" : "Development");

        return new SecurityHeadersMiddleware(_ => Task.CompletedTask, env.Object);
    }

    // ── Headers present in all environments ───────────────────────────────

    [Fact]
    public async Task InvokeAsync_AlwaysSets_XContentTypeOptions()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: false).InvokeAsync(ctx);

        ctx.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysSets_XFrameOptions()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: false).InvokeAsync(ctx);

        ctx.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysSets_ReferrerPolicy()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: false).InvokeAsync(ctx);

        ctx.Response.Headers["Referrer-Policy"].ToString()
           .Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysSets_XPermittedCrossDomainPolicies()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: false).InvokeAsync(ctx);

        ctx.Response.Headers["X-Permitted-Cross-Domain-Policies"].ToString().Should().Be("none");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysSets_ContentSecurityPolicy()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: false).InvokeAsync(ctx);

        ctx.Response.Headers["Content-Security-Policy"].ToString().Should()
           .Contain("default-src 'none'")
           .And.Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task InvokeAsync_AlwaysSets_PermissionsPolicy()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: false).InvokeAsync(ctx);

        ctx.Response.Headers["Permissions-Policy"].ToString().Should()
           .Contain("camera=()").And.Contain("microphone=()");
    }

    // ── HSTS: production only ──────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_InProduction_SetsHsts()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: true).InvokeAsync(ctx);

        ctx.Response.Headers["Strict-Transport-Security"].ToString().Should()
           .Contain("max-age=31536000")
           .And.Contain("includeSubDomains");
    }

    [Fact]
    public async Task InvokeAsync_NotInProduction_DoesNotSetHsts()
    {
        var ctx = CreateContext();
        await CreateMiddleware(isProduction: false).InvokeAsync(ctx);

        ctx.Response.Headers.ContainsKey("Strict-Transport-Security").Should().BeFalse();
    }

    // ── Calls next ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext()
    {
        var nextCalled = false;
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");

        var middleware = new SecurityHeadersMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, env.Object);

        var ctx = CreateContext();
        await middleware.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }
}
