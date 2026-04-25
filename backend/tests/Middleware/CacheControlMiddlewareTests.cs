using Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Orkyo.Foundation.Tests.Middleware;

public class CacheControlMiddlewareTests
{
    private static CacheControlMiddleware CreateMiddleware(RequestDelegate next) => new(next);

    private static DefaultHttpContext CreateHttpContext(string method, string path)
    {
        var feature = new TestHttpResponseFeature();
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Features.Set<IHttpResponseFeature>(feature);
        return context;
    }

    private static async Task FireOnStarting(HttpContext context)
    {
        var feature = context.Features.Get<IHttpResponseFeature>() as TestHttpResponseFeature;
        await feature!.FireOnStartingAsync();
    }

    [Theory]
    [InlineData("/api/criteria")]
    [InlineData("/api/criteria/abc")]
    [InlineData("/api/groups")]
    [InlineData("/api/settings")]
    public async Task GET_CacheablePath_SetsCacheHeader(string path)
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("GET", path);
        await middleware.InvokeAsync(context);
        await FireOnStarting(context);
        context.Response.Headers["Cache-Control"].ToString().Should().Be("private, max-age=60");
    }

    [Theory]
    [InlineData("/api/sites")]
    [InlineData("/api/requests")]
    [InlineData("/api/search")]
    [InlineData("/health/live")]
    public async Task GET_NonCacheablePath_SetsNoCacheHeader(string path)
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("GET", path);
        await middleware.InvokeAsync(context);
        await FireOnStarting(context);
        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-store, no-cache");
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task NonGetMethod_DoesNotSetCacheHeader(string method)
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext(method, "/api/sites");
        await middleware.InvokeAsync(context);
        await FireOnStarting(context);
        context.Response.Headers.ContainsKey("Cache-Control").Should().BeFalse();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task GET_NonSuccessStatus_DoesNotSetCacheHeader(int statusCode)
    {
        var middleware = CreateMiddleware(ctx => { ctx.Response.StatusCode = statusCode; return Task.CompletedTask; });
        var context = CreateHttpContext("GET", "/api/sites");
        await middleware.InvokeAsync(context);
        await FireOnStarting(context);
        context.Response.Headers.ContainsKey("Cache-Control").Should().BeFalse();
    }

    [Fact]
    public async Task GET_ExistingCacheControlHeader_IsNotOverwritten()
    {
        var middleware = CreateMiddleware(ctx => { ctx.Response.Headers["Cache-Control"] = "no-cache"; return Task.CompletedTask; });
        var context = CreateHttpContext("GET", "/api/sites");
        await middleware.InvokeAsync(context);
        await FireOnStarting(context);
        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache");
    }

    [Fact]
    public async Task GET_CacheablePath_IsCaseInsensitive()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = CreateHttpContext("GET", "/API/CRITERIA");
        await middleware.InvokeAsync(context);
        await FireOnStarting(context);
        context.Response.Headers["Cache-Control"].ToString().Should().Be("private, max-age=60");
    }

    private class TestHttpResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));
        public async Task FireOnStartingAsync()
        {
            if (!HasStarted) { HasStarted = true; for (int i = _onStarting.Count - 1; i >= 0; i--) await _onStarting[i].Callback(_onStarting[i].State); }
        }
    }
}
