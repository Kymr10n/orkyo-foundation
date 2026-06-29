using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Admin;

public static class FeedbackAdminEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public static void MapFeedbackAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/feedback")
            .RequireAuthorization()
            .RequireRateLimiting("admin-operations")
            .WithTags("Feedback")
            .WithMetadata(new SkipTenantResolutionAttribute());

        group.MapGet("/", List)
            .RequireSiteAdmin()
            .WithName("AdminListFeedback")
            .WithSummary("List user feedback");

        group.MapGet("/{id:guid}", GetById)
            .RequireSiteAdmin()
            .WithName("AdminGetFeedback")
            .WithSummary("Get a feedback item");

        group.MapPatch("/{id:guid}", Update)
            .RequireSiteAdmin()
            .WithName("AdminUpdateFeedback")
            .WithSummary("Update a feedback item's status / notes / GitHub link");
    }

    private static async Task<IResult> List(
        IFeedbackRepository repository,
        CancellationToken ct,
        string? status = null,
        string? type = null,
        int? limit = null,
        int? offset = null)
    {
        if (status is not null && !FeedbackStatuses.All.Contains(status))
            return Results.BadRequest(new { error = "Unknown status filter" });
        if (type is not null && !FeedbackTypes.All.Contains(type))
            return Results.BadRequest(new { error = "Unknown type filter" });

        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var skip = Math.Max(offset ?? 0, 0);
        var (items, total) = await repository.ListAsync(status, type, take, skip, ct);
        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> GetById(
        Guid id, IFeedbackRepository repository, CancellationToken ct)
    {
        var detail = await repository.GetByIdAsync(id, ct);
        return EndpointHelpers.OkOrNotFound(detail, "Feedback", id);
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateFeedbackRequest request,
        IFeedbackRepository repository,
        IAdminAuditService auditService,
        ICurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct)
    {
        if (request.Status is null && request.AdminNotes is null && request.GithubIssueUrl is null)
            return Results.BadRequest(new { error = "Provide at least one of: status, adminNotes, githubIssueUrl" });
        if (request.Status is not null && !FeedbackStatuses.All.Contains(request.Status))
            return Results.BadRequest(new { error = "Status must be one of: " + string.Join(", ", FeedbackStatuses.All) });

        var current = await repository.GetByIdAsync(id, ct);
        if (current is null)
            return Results.NotFound(new { error = "Feedback not found" });

        var updated = await repository.UpdateAsync(id, request, ct);
        if (updated is null)
            return Results.NotFound(new { error = "Feedback not found" });

        if (request.Status is not null && request.Status != current.Status)
        {
            await auditService.RecordEventAsync(
                principal.UserId, "feedback.status_changed", "feedback", id.ToString(),
                new { previousStatus = current.Status, newStatus = updated.Status }, ct);
            logger.LogInformation("Admin {AdminId} changed feedback {Id} status {From} -> {To}",
                principal.UserId, id, current.Status, updated.Status);
        }

        return Results.Ok(updated);
    }
}
