using System.Text.Json;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Services.Ai;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Ai;

/// <summary>
/// Saved conversations, so a thread survives a reload and follows the person between
/// devices.
///
/// Nothing here participates in a turn: the chat endpoint neither reads nor writes these
/// rows, and the client still echoes the transcript on every turn. Storage is a notebook
/// beside the conversation, not a dependency of it — if every one of these calls failed,
/// the assistant would still answer.
///
/// Every route acts on the caller's own conversations; the service never takes an owner
/// from the request. Someone else's id is indistinguishable from one that does not exist.
/// </summary>
public static class AiConversationEndpoints
{
    public static void MapAiConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/conversations")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite()
            .WithTags("AI Assistant");

        group.MapGet("/", List)
            .WithName("ListAiConversations")
            .WithSummary("List the caller's saved conversations, newest first");

        group.MapGet("/{id:guid}", Get)
            .WithName("GetAiConversation")
            .WithSummary("Read one of the caller's saved conversations in full");

        // Saving is not editing the workspace, so it stays open to every member the
        // assistant itself is open to — a viewer may converse, and may keep what they said.
        group.MapPut("/{id:guid}", Save)
            .AllowMemberWrite()
            .WithName("SaveAiConversation")
            .WithSummary("Create or replace one of the caller's conversations");

        group.MapDelete("/{id:guid}", Delete)
            .AllowMemberWrite()
            .WithName("DeleteAiConversation")
            .WithSummary("Delete one of the caller's conversations");
    }

    private static async Task<IResult> List(IAiConversationService conversations, CancellationToken ct)
        => Results.Ok(await conversations.ListAsync(ct));

    private static async Task<IResult> Get(Guid id, IAiConversationService conversations, CancellationToken ct)
    {
        var found = await conversations.GetAsync(id, ct);
        if (found is null) return Results.NotFound();

        return Results.Ok(new AiConversationResponse
        {
            Id = found.Id,
            Title = found.Title,
            Entries = found.Entries,
            Transcript = found.Transcript,
            UpdatedAt = found.UpdatedAt,
        });
    }

    private static async Task<IResult> Save(
        Guid id,
        SaveAiConversationRequest request,
        IValidator<SaveAiConversationRequest> validator,
        IAiConversationService conversations,
        CancellationToken ct)
    {
        var shape = await validator.ValidateAsync(request, ct);
        if (!shape.IsValid)
            return ProblemResults.Problem(StatusCodes.Status400BadRequest,
                Api.Constants.ErrorCodes.ValidationError,
                detail: "One or more fields failed validation.", errors: shape.ToDictionary());

        await conversations.SaveAsync(
            id,
            request.Title,
            request.Entries.GetRawText(),
            request.Transcript.GetRawText(),
            ct);

        return Results.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, IAiConversationService conversations, CancellationToken ct)
        => await conversations.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
}

/// <summary>
/// A saved conversation as the client reads it back. Typed like its sibling AI responses
/// rather than an anonymous object, and the blobs stay JSON so the client does not parse
/// a second time.
/// </summary>
public sealed record AiConversationResponse
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required JsonElement Entries { get; init; }
    public required JsonElement Transcript { get; init; }
    public required DateTime UpdatedAt { get; init; }
}
