using Api.Middleware;
using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class FeedbackEndpoints
{
    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/feedback").WithTags("Feedback").RequireAuthorization().RequireTenantMembership();

        group.MapPost("/", async (CreateFeedbackRequest request, HttpContext ctx, ICurrentPrincipal currentPrincipal, IFeedbackRepository repository, ILogger<EndpointLoggerCategory> logger) =>
        {
            var validationErrors = ValidateRequest(request);
            if (validationErrors.Count > 0)
                return ErrorResponses.BadRequest(string.Join("; ", validationErrors));

            Guid? userId = currentPrincipal.IsAuthenticated ? currentPrincipal.UserId : null;
            var userAgent = ctx.Request.Headers.UserAgent.FirstOrDefault();

            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var feedback = await repository.CreateAsync(request, userId, userAgent);
                logger.LogInformation("Feedback submitted: {Id} - {Type}: {Title}", feedback.Id, feedback.FeedbackType, feedback.Title);
                return Results.Created($"/feedback/{feedback.Id}", feedback);
            }, logger, "submit feedback", new { request.FeedbackType, request.Title });
        })
        .WithName("SubmitFeedback")
        .WithSummary("Submit user feedback");
    }

    private static List<string> ValidateRequest(CreateFeedbackRequest request)
    {
        var errors = new List<string>();
        var validTypes = new[] { "bug", "feature", "question", "other" };
        if (string.IsNullOrWhiteSpace(request.FeedbackType) || !validTypes.Contains(request.FeedbackType.ToLower()))
            errors.Add("FeedbackType must be one of: bug, feature, question, other");
        if (string.IsNullOrWhiteSpace(request.Title))
            errors.Add("Title is required");
        else if (request.Title.Length > DomainLimits.FeedbackTitleMaxLength)
            errors.Add($"Title must be {DomainLimits.FeedbackTitleMaxLength} characters or less");
        return errors;
    }
}
