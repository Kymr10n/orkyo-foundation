using Api.Constants;
using Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Middleware;

public class CorrelationIdMiddlewareTests
{
    private static CorrelationIdMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, NullLogger<CorrelationIdMiddleware>.Instance);

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task GeneratesCorrelationId_WhenNotSuppliedByClient()
    {
        string? capturedId = null;
        var middleware = CreateMiddleware(ctx => { capturedId = ctx.TraceIdentifier; return Task.CompletedTask; });
        await middleware.InvokeAsync(CreateHttpContext());
        capturedId.Should().NotBeNull();
        Guid.TryParse(capturedId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task UsesClientCorrelationId_WhenSuppliedInRequestHeader()
    {
        const string clientId = "client-supplied-id-12345";
        string? capturedId = null;
        var middleware = CreateMiddleware(ctx => { capturedId = ctx.TraceIdentifier; return Task.CompletedTask; });
        var context = CreateHttpContext();
        context.Request.Headers[HeaderConstants.CorrelationId] = clientId;
        await middleware.InvokeAsync(context);
        capturedId.Should().Be(clientId);
    }

    [Fact]
    public async Task RegistersOnStartingCallback_ThatSetsResponseHeader()
    {
        var feature = new TestHttpResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(feature);
        await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        await feature.FireOnStartingAsync();
        feature.Headers.ContainsKey(HeaderConstants.CorrelationId).Should().BeTrue();
        Guid.TryParse(feature.Headers[HeaderConstants.CorrelationId].ToString(), out _).Should().BeTrue();
    }

    [Fact]
    public async Task ResponseHeader_MatchesClientCorrelationId()
    {
        const string clientId = "matching-test-id-67890";
        var feature = new TestHttpResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(feature);
        context.Request.Headers[HeaderConstants.CorrelationId] = clientId;
        await CreateMiddleware(_ => Task.CompletedTask).InvokeAsync(context);
        await feature.FireOnStartingAsync();
        feature.Headers[HeaderConstants.CorrelationId].ToString().Should().Be(clientId);
    }

    [Fact]
    public async Task CallsNextMiddleware()
    {
        var nextCalled = false;
        await CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }).InvokeAsync(CreateHttpContext());
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task GeneratesUniqueIds_ForDifferentRequests()
    {
        var ids = new List<string>();
        var middleware = CreateMiddleware(ctx => { ids.Add(ctx.TraceIdentifier); return Task.CompletedTask; });
        for (int i = 0; i < 10; i++) await middleware.InvokeAsync(CreateHttpContext());
        ids.Distinct().Count().Should().Be(10);
    }

    private class TestHttpResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = new();
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
