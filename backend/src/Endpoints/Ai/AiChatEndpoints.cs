using System.Text.Json;
using Api.Middleware;
using Api.Services.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Ai;

/// <summary>Wire shape for one chat turn.</summary>
public sealed record AiChatEndpointRequest
{
    public string? Message { get; init; }
    public List<AiMessage>? Transcript { get; init; }
    public AiChatContext? Context { get; init; }
    public AiProposalOutcome? PendingToolResult { get; init; }
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
        CancellationToken ct)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        // Ask any reverse proxy in front of us not to buffer, so the stream stays a stream
        // without needing proxy configuration changed for this one endpoint.
        http.Response.Headers["X-Accel-Buffering"] = "no";

        var turn = new AiChatRequest
        {
            Message = request.Message,
            Transcript = request.Transcript ?? [],
            ContextRequestId = request.Context?.RequestId,
            ContextConflictKind = request.Context?.Kind,
            PendingToolResult = request.PendingToolResult,
        };

        await foreach (var evt in chat.RunTurnAsync(turn, ct))
        {
            var (name, payload) = Describe(evt);
            await WriteEventAsync(http.Response, name, payload, ct);
        }
    }

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
        AiChatEvent.Transcript t => ("transcript", t.Messages),
        AiChatEvent.Error e => ("error", new { code = e.Code, message = e.Detail }),
        _ => ("done", new { }),
    };

    private static async Task WriteEventAsync(HttpResponse response, string name, object payload, CancellationToken ct)
    {
        await response.WriteAsync($"event: {name}\n", ct);
        await response.WriteAsync($"data: {JsonSerializer.Serialize(payload, Json)}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
