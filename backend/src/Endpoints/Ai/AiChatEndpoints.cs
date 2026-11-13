using Api.Helpers;
using Api.Middleware;
using Api.Services.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Api.Endpoints.Ai;

/// <summary>Wire shape for one chat turn.</summary>
public sealed record AiChatEndpointRequest
{
    public string? Message { get; init; }
    public List<AiMessage>? Transcript { get; init; }
    public AiChatContext? Context { get; init; }
    public AiProposalOutcome? PendingToolResult { get; init; }

    /// <summary>The site the person has selected, when they have one.</summary>
    public Guid? SiteId { get; init; }
}

/// <summary>Where the person opened the assistant from, when that matters.</summary>
public sealed record AiChatContext
{
    public string? Type { get; init; }
    public Guid? RequestId { get; init; }
    public string? Kind { get; init; }
}

/// <summary>
/// The chat turn, streamed.
///
/// A turn is a loop of provider calls and tool look-ups and routinely runs for tens of
/// seconds. Streaming keeps bytes moving so an idle-timeout in front of the application
/// cannot cut a healthy turn short, and it lets the panel say what the assistant is doing
/// rather than showing a still spinner.
///
/// The events are coarse — status, prose, proposal, transcript — rather than token
/// deltas. That is enough for the UI and keeps this endpoint simple; per-token streaming
/// can be added inside the same event vocabulary later.
/// </summary>
public static class AiChatEndpoints
{
    public static void MapAiChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite()
            .WithTags("AI Assistant");

        // A chat turn persists nothing, so it is a non-mutating POST — the same shape as
        // the auto-schedule preview, and open to any member the access rules allow.
        group.MapPost("/chat", Chat)
            .AllowMemberWrite()
            .WithName("AiChat")
            .WithSummary("Run one assistant turn and stream what happens");
    }

    private static async Task Chat(
        HttpContext http,
        AiChatEndpointRequest request,
        IAiChatService chat,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(AiChatEndpoints));

        ServerSentEvents.Start(http.Response);

        var turn = new AiChatRequest
        {
            Message = request.Message,
            Transcript = request.Transcript ?? [],
            ContextRequestId = request.Context?.RequestId,
            ContextConflictKind = request.Context?.Kind,
            PendingToolResult = request.PendingToolResult,
            SiteId = request.SiteId,
        };

        try
        {
            await StreamWithHeartbeatAsync(http.Response, chat.RunTurnAsync(turn, ct), ServerSentEvents.HeartbeatInterval, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The person closed the panel or navigated away mid-turn. The service has
            // already recorded the tokens spent; there is nobody left to write to.
        }
        catch (Exception ex)
        {
            // Anything else — most often the turn's own five-minute deadline, which is a
            // linked token and so does NOT set ct.IsCancellationRequested. Letting it
            // escape here ends the response mid-stream with no terminating event, and the
            // panel reports a broken stream instead of what happened.
            //
            // Headers are long gone by this point, so the only way to say anything is to
            // write it into the stream that is already open.
            logger.LogWarning(ex, "AI turn ended abnormally");
            await WriteTurnFailureAsync(http.Response, ex, logger);
        }
    }

    /// <remarks>Internal so tests can drive it with an interval short enough to observe.</remarks>
    internal static Task StreamWithHeartbeatAsync(
        HttpResponse response, IAsyncEnumerable<AiChatEvent> events, TimeSpan heartbeatInterval, CancellationToken ct) =>
        ServerSentEvents.StreamAsync(response, events, Describe, heartbeatInterval, ct);

    private static (string Name, object Payload) Describe(AiChatEvent evt) => evt switch
    {
        AiChatEvent.Status s => ("status", new { phase = s.Phase, tool = s.Tool }),
        AiChatEvent.Message m => ("message", new { text = m.Text }),
        AiChatEvent.Proposal p => ("proposal", new
        {
            toolUseId = p.Value.ToolUseId,
            kind = p.Value.Kind,
            input = p.Value.InputJson,
        }),
        AiChatEvent.UiAction u => ("ui", new { view = u.View, entityId = u.EntityId, siteId = u.SiteId }),
        AiChatEvent.Transcript t => ("transcript", t.Messages),
        AiChatEvent.Error e => ("error", new { code = e.Code, message = e.Detail }),
        _ => ("done", new { }),
    };

    /// <summary>
    /// Closes a broken turn from inside the stream that is already open.
    ///
    /// A turn that dies after the headers are sent has no status code left to use, so the
    /// only way to say anything is an error event followed by done — which is what the
    /// panel waits for. Failing to write that leaves it reading a truncated stream.
    /// </summary>
    /// <remarks>Internal so the recovery can be tested without an HTTP stack.</remarks>
    internal static async Task WriteTurnFailureAsync(HttpResponse response, Exception ex, ILogger logger)
    {
        // The turn's own deadline is a linked token, so it arrives here rather than in the
        // caller's cancellation filter — and it is the one failure worth naming precisely,
        // because the person can act on it by asking something narrower.
        var timedOut = ex is OperationCanceledException;

        try
        {
            await ServerSentEvents.WriteEventAsync(response, "error", new
            {
                code = timedOut ? "turn_timeout" : "turn_failed",
                message = timedOut
                    ? "That took longer than the assistant is allowed to spend. Try a narrower question."
                    : "The assistant stopped before it finished. Try again.",
            }, CancellationToken.None);
            await ServerSentEvents.WriteEventAsync(response, "done", new { }, CancellationToken.None);
        }
        catch (Exception writeFailure)
        {
            // The connection is gone too. Nothing left to tell anyone.
            logger.LogDebug(writeFailure, "Could not report the turn failure to the client");
        }
    }
}
