using System.Text;
using Api.Endpoints;
using Api.Helpers;
using Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// The answer to an import: a status code while the import has reported nothing, and a
/// stream of events from its first report on.
/// </summary>
public class TransferImportStreamTests
{
    private static readonly TimeSpan NoBeat = TimeSpan.FromMinutes(1);
    private static readonly TransferImportResult Done = new() { Rows = 7, MatchedUsers = 2, PlaceholderUsers = 1 };

    private static DefaultHttpContext Context() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private static string Written(HttpContext ctx) =>
        Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());

    [Fact]
    public async Task AnImportThatReports_IsAStreamOfItsStepsAndThenItsResult()
    {
        var ctx = Context();

        await TransferEndpoints.StreamImportAsync(ctx, report =>
        {
            report(new TransferProgress(TransferProgress.Erase));
            report(new TransferProgress(TransferProgress.Load, 40));
            return Task.FromResult(Done);
        }, NoBeat, default);

        ctx.Response.ContentType.Should().Be("text/event-stream");
        ctx.Response.Headers["X-Accel-Buffering"].ToString().Should().Be("no");
        Written(ctx).Should().Be(
            "event: progress\ndata: {\"step\":\"erase\",\"percent\":null}\n\n" +
            "event: progress\ndata: {\"step\":\"load\",\"percent\":40}\n\n" +
            "event: result\ndata: {\"rows\":7,\"matchedUsers\":2,\"placeholderUsers\":1}\n\n");
    }

    [Fact]
    public async Task AnImportThatFailsBeforeItReports_KeepsItsStatusCode()
    {
        var ctx = Context();

        var act = () => TransferEndpoints.StreamImportAsync(ctx, _ =>
            Task.FromException<TransferImportResult>(new TransferRejectedException(409, ApiErrorCodes.TransferBusy, "busy")),
            NoBeat, default);

        // The exception handler answers it, like any other request: nothing is written here.
        await act.Should().ThrowAsync<TransferRejectedException>();
        ctx.Response.ContentType.Should().BeNull();
        Written(ctx).Should().BeEmpty();
    }

    [Fact]
    public async Task AnImportThatIsRefusedAfterItReports_EndsWithTheSameProblemAsAnEvent()
    {
        var ctx = Context();

        await TransferEndpoints.StreamImportAsync(ctx, report =>
        {
            report(new TransferProgress(TransferProgress.Erase));
            throw new TransferRejectedException(422, ApiErrorCodes.TransferArchiveInvalid, "The archive is not valid.");
        }, NoBeat, default);

        var written = Written(ctx);
        written.Should().Contain("event: error\n");
        written.Should().Contain("\"status\":422");
        written.Should().Contain($"\"code\":\"{ApiErrorCodes.TransferArchiveInvalid}\"");
        written.Should().Contain("\"detail\":\"The archive is not valid.\"");
        written.Should().NotContain("event: result");
    }

    [Fact]
    public async Task AFaultAfterTheImportReports_IsAnErrorEventWithoutItsMessage()
    {
        var ctx = Context();

        await TransferEndpoints.StreamImportAsync(ctx, report =>
        {
            report(new TransferProgress(TransferProgress.Erase));
            throw new InvalidOperationException("connection string of the internal host");
        }, NoBeat, default);

        var written = Written(ctx);
        written.Should().Contain("event: error\n");
        written.Should().Contain("\"status\":500");
        written.Should().Contain($"\"code\":\"{ApiErrorCodes.TransferFailed}\"");
        written.Should().NotContain("internal host");
    }

    [Fact]
    public async Task ASilentStep_IsBridgedWithKeepAlives()
    {
        var ctx = Context();
        var release = new TaskCompletionSource();

        var streaming = TransferEndpoints.StreamImportAsync(ctx, async report =>
        {
            report(new TransferProgress(TransferProgress.Erase));
            await release.Task;
            return Done;
        }, TimeSpan.FromMilliseconds(20), default);

        while (!Written(ctx).Contains(": keep-alive")) await Task.Delay(5);
        release.SetResult();
        await streaming;

        Written(ctx).Should().EndWith("event: result\ndata: {\"rows\":7,\"matchedUsers\":2,\"placeholderUsers\":1}\n\n");
    }

    [Fact]
    public async Task ACallerWhoLeaves_GetsNoErrorEvent_AndTheImportIsAwaited()
    {
        var ctx = Context();
        using var cts = new CancellationTokenSource();
        var ended = false;

        await TransferEndpoints.StreamImportAsync(ctx, async report =>
        {
            report(new TransferProgress(TransferProgress.Erase));
            try { await Task.Delay(Timeout.Infinite, cts.Token); }
            finally { ended = true; }
            return Done;
        }, TimeSpan.FromMilliseconds(20), CancelSoon(cts));

        ended.Should().BeTrue();
        Written(ctx).Should().NotContain("event: error").And.NotContain("event: result");
    }

    private static CancellationToken CancelSoon(CancellationTokenSource cts)
    {
        cts.CancelAfter(TimeSpan.FromMilliseconds(60));
        return cts.Token;
    }

    [Fact]
    public async Task AnImportThatReportsNothing_AnswersWithItsResultAsJson()
    {
        var ctx = Context();

        await TransferEndpoints.StreamImportAsync(ctx, _ => Task.FromResult(Done), NoBeat, default);

        ctx.Response.ContentType.Should().StartWith("application/json");
        Written(ctx).Should().Contain("\"rows\":7");
    }
}
