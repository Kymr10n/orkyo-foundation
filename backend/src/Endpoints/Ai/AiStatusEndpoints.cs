using Api.Middleware;
using Api.Services.Ai;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Ai;

/// <summary>
/// What the current user may do with the assistant. Members read this to decide whether
/// to offer the assistant at all, and to show the remaining budget.
///
/// Carries no secrets and no key hint — the answer is only ever about the caller.
/// </summary>
public static class AiStatusEndpoints
{
    public static void MapAiStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite()
            .WithTags("AI Assistant");

        group.MapGet("/status", GetStatus)
            .WithName("GetAiStatus")
            .WithSummary("Report whether the caller can use the assistant, and their remaining budget");
    }

    private static async Task<IResult> GetStatus(IAiAccessService access, CancellationToken ct)
        => Results.Ok(await access.GetStatusAsync(ct));
}
