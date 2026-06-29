using System.Net;
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
        if (string.IsNullOrEmpty(notifyEmail)) return;

        try
        {
            string Enc(string? s) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(s) ? "—" : s);
            var subject = $"[Feedback] {request.FeedbackType}: {request.Title}";
            var htmlBody =
                "<h2>New feedback</h2><table>" +
                $"<tr><td><strong>Type</strong></td><td>{Enc(request.FeedbackType)}</td></tr>" +
                $"<tr><td><strong>Title</strong></td><td>{Enc(request.Title)}</td></tr>" +
                $"<tr><td><strong>From</strong></td><td>{Enc(submitterEmail)}</td></tr>" +
                $"<tr><td><strong>Tenant</strong></td><td>{Enc(tenantSlug)}</td></tr>" +
                $"<tr><td><strong>Page</strong></td><td>{Enc(request.PageUrl)}</td></tr>" +
                $"<tr><td><strong>User agent</strong></td><td>{Enc(userAgent)}</td></tr>" +
                "</table><hr/>" +
                $"<p>{Enc(request.Description).Replace("\n", "<br/>")}</p>";
            var textBody =
                $"Type: {request.FeedbackType}\nTitle: {request.Title}\nFrom: {submitterEmail}\n" +
                $"Tenant: {tenantSlug}\nPage: {request.PageUrl ?? "—"}\n\n{request.Description ?? ""}";
            await emailService.SendEmailAsync(notifyEmail, "Orkyo Team", subject, htmlBody, textBody);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Failed to send feedback notification email"); }
    }
}
