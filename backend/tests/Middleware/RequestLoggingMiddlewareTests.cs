using Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Orkyo.Foundation.Tests.Middleware;

public class RequestLoggingMiddlewareTests
{
    private readonly Mock<ILogger<RequestLoggingMiddleware>> _logger = new();

    private RequestLoggingMiddleware CreateMiddleware(RequestDelegate next)
        => new(next, _logger.Object);

    private static DefaultHttpContext CreateContext(string method = "GET", string path = "/api/test")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    // ── 2xx — Information level ────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_Success_LogsAtInformationLevel()
    {
        var ctx = CreateContext();
        ctx.Response.StatusCode = 200;

        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(ctx);

        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("200")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task InvokeAsync_NonSuccess_LogsAtWarningLevel(int statusCode)
    {
        var ctx = CreateContext();

        var middleware = CreateMiddleware(c =>
        {
            c.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── Exception path ─────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_LogsErrorAndRethrows()
    {
        var ctx = CreateContext("POST", "/api/widgets");
        var ex = new InvalidOperationException("boom");

        var middleware = CreateMiddleware(_ => throw ex);

        var act = () => middleware.InvokeAsync(ctx);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                ex,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── Client cancellation (499) — quiet Information, not Error ────────────

    [Fact]
    public async Task InvokeAsync_ClientCancellation_LogsInformationNotError()
    {
        var ctx = CreateContext();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        ctx.RequestAborted = cts.Token;

        var middleware = CreateMiddleware(_ => throw new OperationCanceledException(cts.Token));

        var act = () => middleware.InvokeAsync(ctx);
        await act.Should().ThrowAsync<OperationCanceledException>();

        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("499")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_CancellationNotFromClient_LogsError()
    {
        // RequestAborted is NOT cancelled (e.g. a server-side timeout token) → surfaced loudly,
        // never swallowed as a quiet client-cancel.
        var ctx = CreateContext();
        var middleware = CreateMiddleware(_ => throw new OperationCanceledException());

        var act = () => middleware.InvokeAsync(ctx);
        await act.Should().ThrowAsync<OperationCanceledException>();

        _logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── Calls next delegate ────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_AlwaysCallsNext()
    {
        var nextCalled = false;
        var ctx = CreateContext();

        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }
}
