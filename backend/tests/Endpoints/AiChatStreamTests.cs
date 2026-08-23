using System.Text;
using Api.Endpoints.Ai;
using Api.Services.Ai;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Covers the SSE keepalive on the assistant's chat stream.
///
/// A turn spends most of its time waiting on the model and writes nothing while it does.
/// Without a keepalive that silence is indistinguishable from a dead connection, and the
/// first proxy or browser to lose patience ends the turn — which is exactly how a long
/// turn failed before this existed. The client already skips comment lines, so a comment
/// is traffic to the network and invisible to the reader.
/// </summary>
public class AiChatStreamTests
{
    private static readonly TimeSpan Beat = TimeSpan.FromMilliseconds(40);

    private static (HttpResponse Response, MemoryStream Body) CreateResponse()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context.Response, body);
    }

    private static string ReadBody(MemoryStream body) => Encoding.UTF8.GetString(body.ToArray());

    /// <summary>An event stream that stays silent for <paramref name="gap"/> before its single event.</summary>
    private static async IAsyncEnumerable<AiChatEvent> SlowStream(TimeSpan gap)
    {
        await Task.Delay(gap);
        yield return new AiChatEvent.Message("done thinking");
    }

    private static async IAsyncEnumerable<AiChatEvent> ImmediateStream()
    {
        yield return new AiChatEvent.Message("here at once");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ASilentTurn_EmitsKeepalivesWhileItWaits()
    {
        var (response, body) = CreateResponse();

        await AiChatEndpoints.StreamWithHeartbeatAsync(
            response, SlowStream(TimeSpan.FromMilliseconds(260)), Beat, default);

        var written = ReadBody(body);

        Assert.Contains(": keep-alive", written);
        // The gap spans several intervals, so silence must produce more than one.
        Assert.True(written.Split(": keep-alive").Length - 1 >= 2, written);
    }

    [Fact]
    public async Task TheRealEventStillArrives_AfterTheKeepalives()
    {
        var (response, body) = CreateResponse();

        await AiChatEndpoints.StreamWithHeartbeatAsync(
            response, SlowStream(TimeSpan.FromMilliseconds(120)), Beat, default);

        var written = ReadBody(body);

        Assert.Contains("event: message", written);
        Assert.Contains("done thinking", written);
        // Order matters: a keepalive is filler while waiting, never a replacement.
        Assert.True(written.IndexOf(": keep-alive", StringComparison.Ordinal)
                    < written.IndexOf("event: message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATurnThatNeverGoesQuiet_SendsNoKeepalives()
    {
        var (response, body) = CreateResponse();

        await AiChatEndpoints.StreamWithHeartbeatAsync(
            response, ImmediateStream(), Beat, default);

        var written = ReadBody(body);

        Assert.DoesNotContain(": keep-alive", written);
        Assert.Contains("event: message", written);
    }

    /// <summary>A stream that fails on write — a client whose connection has died.</summary>
    private sealed class BrokenStream : MemoryStream
    {
        /// <summary>Completes when the first write is attempted, so the test can react to the
        /// failure instead of guessing when it happens.</summary>
        public TaskCompletionSource WriteAttempted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            WriteAttempted.TrySetResult();
            throw new IOException("connection reset");
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            WriteAttempted.TrySetResult();
            throw new IOException("connection reset");
        }
    }

    /// <summary>A turn stuck waiting on the model: its advance stays pending until cancelled.</summary>
    private static async IAsyncEnumerable<AiChatEvent> StuckStream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        yield return new AiChatEvent.Message("unreachable");
    }

    [Fact]
    public async Task AWriteFailureMidTurn_SurfacesTheWriteError_NotTheIteratorsDisposeRefusal()
    {
        // The production incident: a heartbeat write failed while the turn was still
        // waiting on the model. Disposing an async iterator with an advance in flight
        // throws NotSupportedException, which masked the real error and reset the stream.
        var context = new DefaultHttpContext();
        var body = new BrokenStream();
        context.Response.Body = body;
        using var turnCts = new CancellationTokenSource();

        var streaming = AiChatEndpoints.StreamWithHeartbeatAsync(
            context.Response, StuckStream(turnCts.Token), Beat, turnCts.Token);

        // Wait for the heartbeat to actually hit the broken stream — a timer here races
        // suite load — then cancel, the way RequestAborted fires once Kestrel notices the
        // connection is gone. The pending advance must complete for disposal to be legal.
        await body.WriteAttempted.Task;
        turnCts.Cancel();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => streaming);

        Assert.IsType<IOException>(thrown);
    }

    [Fact]
    public async Task KeepalivesAreSseComments_SoTheClientIgnoresThem()
    {
        var (response, body) = CreateResponse();

        await AiChatEndpoints.StreamWithHeartbeatAsync(
            response, SlowStream(TimeSpan.FromMilliseconds(100)), Beat, default);

        // Every keepalive line must start with ':' and end the record with a blank line,
        // or the client's parser would treat it as a malformed event instead of skipping it.
        foreach (var line in ReadBody(body).Split('\n'))
        {
            if (line.Contains("keep-alive"))
                Assert.StartsWith(":", line);
        }
    }
}
