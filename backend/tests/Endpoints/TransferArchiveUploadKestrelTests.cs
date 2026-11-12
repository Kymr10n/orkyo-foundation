using System.Net;
using Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// The archive upload on a real Kestrel server. The products cap every request body, and the
/// in-memory test server has no such cap, so only this host can show that the transfer route
/// lifts it.
/// </summary>
public class TransferArchiveUploadKestrelTests : IAsyncLifetime
{
    private const int ServerBodyLimit = 1024;
    private const int ArchiveCeiling = 1024 * 1024;

    private readonly WebApplication _app;
    private HttpClient _client = null!;

    public TransferArchiveUploadKestrelTests()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0);
            kestrel.Limits.MaxRequestBodySize = ServerBodyLimit;
        });
        _app = builder.Build();

        _app.MapPost("/transfer", async (HttpContext ctx, CancellationToken ct) =>
        {
            await using var archive = await TransferEndpoints.ReceiveArchiveAsync(ctx, ArchiveCeiling, ct);
            return Results.Ok(archive.Length);
        });
        _app.MapPost("/other", async (HttpContext ctx, CancellationToken ct) =>
        {
            await ctx.Request.Body.CopyToAsync(Stream.Null, ct);
            return Results.Ok();
        });
    }

    public async Task InitializeAsync()
    {
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static HttpContent Body(int bytes, bool chunked) =>
        chunked ? new StreamContent(new MemoryStream(new byte[bytes])) : new ByteArrayContent(new byte[bytes]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnArchiveAboveTheServerBodyLimit_IsReceivedInFull(bool chunked)
    {
        const int size = 64 * ServerBodyLimit;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/transfer") { Content = Body(size, chunked) };
        request.Headers.TransferEncodingChunked = chunked;

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(size.ToString());
    }

    [Fact]
    public async Task ABodyThatWasAlreadyRead_IsAnError_NotAnUploadCutShortByTheServerLimit()
    {
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>(new ReadOnlyBodySize());

        var act = () => TransferEndpoints.ReceiveArchiveAsync(ctx, ArchiveCeiling, default);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("body size limit");
    }

    private sealed class ReadOnlyBodySize : Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => true;
        public long? MaxRequestBodySize { get; set; } = ServerBodyLimit;
    }

    [Fact]
    public async Task TheServerBodyLimit_StillHoldsForOtherRoutes()
    {
        var response = await _client.PostAsync("/other", Body(64 * ServerBodyLimit, chunked: false));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }
}
