using Api.Configuration;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Orkyo.Shared;

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
            ICurrentTenant currentTenant,
            IFeedbackRepository repository,
            IEmailService emailService,
            IConfiguration configuration,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                Guid? userId = currentPrincipal.IsAuthenticated ? currentPrincipal.UserId : null;
                var userAgent = ctx.Request.Headers.UserAgent.FirstOrDefault();
                var feedback = await repository.CreateAsync(request, userId, userAgent, ct);
                logger.LogInformation("Feedback submitted: {Id} - {Type}: {Title}", feedback.Id, feedback.FeedbackType, feedback.Title);

                await NotifyAsync(request, currentPrincipal.Email, currentTenant.TenantSlug, userAgent,
                    emailService, configuration, logger);

                return Results.Created($"/feedback/{feedback.Id}", feedback);
            }, logger, "submit feedback", new { request.FeedbackType, request.Title });
        })
        .WithName("SubmitFeedback")
        .WithSummary("Submit user feedback");
    }

    // Best-effort admin notification — a mail failure must never fail the submission.
    private static async Task NotifyAsync(
        CreateFeedbackRequest request, string submitterEmail, string tenantSlug, string? userAgent,
        IEmailService emailService, IConfiguration configuration, ILogger logger)
    {
        var notifyEmail = configuration.GetOptionalString(ConfigKeys.FeedbackNotificationEmail);

        var subject = $"[Feedback] {request.FeedbackType}: {request.Title}";
        var (htmlBody, textBody) = EmailTemplates.AdminNotification("New feedback",
            [
                ("Type", request.FeedbackType),
                ("Title", request.Title),
                ("From", submitterEmail),
                ("Tenant", tenantSlug),
                ("Page", request.PageUrl),
                ("User agent", userAgent),
            ],
            request.Description);

        await emailService.TrySendNotificationAsync(notifyEmail, subject, htmlBody, textBody, logger, "feedback notification email");
    }
}
