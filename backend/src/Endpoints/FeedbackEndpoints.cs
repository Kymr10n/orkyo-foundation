using Api.Constants;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class FeedbackEndpoints
{
    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/feedback").WithTags("Feedback").RequireAuthorization().RequireTenantMembership();

        group.MapPost("/", async (
            CreateFeedbackRequest request,
            IValidator<CreateFeedbackRequest> validator,
            HttpContext ctx,
            ICurrentPrincipal currentPrincipal,
            IFeedbackRepository repository,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                Guid? userId = currentPrincipal.IsAuthenticated ? currentPrincipal.UserId : null;
                var userAgent = ctx.Request.Headers.UserAgent.FirstOrDefault();
                var feedback = await repository.CreateAsync(request, userId, userAgent);
                logger.LogInformation("Feedback submitted: {Id} - {Type}: {Title}", feedback.Id, feedback.FeedbackType, feedback.Title);
                return Results.Created($"/feedback/{feedback.Id}", feedback);
            }, logger, "submit feedback", new { request.FeedbackType, request.Title });
        })
        .WithName("SubmitFeedback")
        .WithSummary("Submit user feedback");
    }
}
