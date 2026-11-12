using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Api.Helpers;

/// <summary>
/// A response that is a Server-Sent Events stream: named events with a JSON payload, and a
/// comment line as the keep-alive. Shared by every endpoint that reports what it is doing
/// while it does it.
/// </summary>
public static class ServerSentEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// How long a stream may stay silent. Anything between us and the browser — a reverse
    /// proxy, a CDN, a corporate middlebox — is entitled to drop a connection it believes
    /// has gone idle. A comment line costs nothing and resets every such timer.
    /// </summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>Sets the headers of a stream. Call before the first write.</summary>
    public static void Start(HttpResponse response)
    {
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        // Ask any reverse proxy in front of us not to buffer, so the stream stays a stream
        // without needing proxy configuration changed for the endpoint.
        response.Headers["X-Accel-Buffering"] = "no";
    }

    /// <summary>
    /// Writes <paramref name="events"/> as they arrive, emitting an SSE comment whenever the
    /// gap since the last write reaches <paramref name="heartbeatInterval"/>. Comments are the
    /// protocol's own keep-alive: the client skips any line starting with ':' and sees only
    /// real events.
    /// </summary>
    public static async Task StreamAsync<T>(
        HttpResponse response, IAsyncEnumerable<T> events, Func<T, (string Name, object Payload)> describe,
        TimeSpan heartbeatInterval, CancellationToken ct)
    {
        var enumerator = events.GetAsyncEnumerator(ct);
        Task<bool>? pending = null;
        try
        {
            while (true)
            {
                pending = enumerator.MoveNextAsync().AsTask();

                // Wait for the next event, but never longer than the heartbeat interval. The
                // linked source stops the pending delay as soon as the next event is there, so
                // a long stream does not accumulate timers.
                while (true)
                {
                    using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var delay = Task.Delay(heartbeatInterval, beat.Token);
                    var winner = await Task.WhenAny(pending, delay);
                    beat.Cancel();

                    if (winner == pending) break;
                    await WriteHeartbeatAsync(response, ct);
                }

                var moved = await pending;
                pending = null;
                if (!moved) break;

                var (name, payload) = describe(enumerator.Current);
                await WriteEventAsync(response, name, payload, ct);
            }
        }
        finally
        {
            // An async iterator refuses DisposeAsync while a MoveNextAsync is still in
            // flight (NotSupportedException) — and on a failure path that refusal would
            // mask the exception that actually brought us here. Drain the pending advance
            // first; it completes promptly because the iterator observes the same ct, and
            // its own outcome no longer matters.
            if (pending is not null)
            {
                try { await pending; }
                catch
                {
                    // Deliberately swallowed: surfacing the original exception is the point.
                }
            }
            await enumerator.DisposeAsync();
        }
    }

    public static async Task WriteEventAsync(HttpResponse response, string name, object payload, CancellationToken ct)
    {
        await response.WriteAsync($"event: {name}\n", ct);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, Json)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>An SSE comment: ignored by the client, but traffic as far as the network is concerned.</summary>
    private static async Task WriteHeartbeatAsync(HttpResponse response, CancellationToken ct)
    {
        await response.WriteAsync(": keep-alive\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
