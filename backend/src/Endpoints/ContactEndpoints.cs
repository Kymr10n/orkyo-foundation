using Api.Configuration;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Security.Challenge;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Orkyo.Shared;

namespace Api.Endpoints;

public static class ContactEndpoints
{
    public static void MapContactEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/contact")
            .AllowAnonymous()
            .WithMetadata(new SkipTenantResolutionAttribute())
            .WithTags("Contact");

        group.MapPost("/", async (
            [FromBody] ContactRequest request,
            IValidator<ContactRequest> validator,
            IContactSubmissionRepository contactSubmissions,
            IEmailService emailService,
            IConfiguration configuration,
            TimeProvider time,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                await contactSubmissions.AddAsync(request, time.GetUtcNow().UtcDateTime, ct);

                logger.LogInformation("Contact form submitted (subject={Subject})", request.Subject);

                var notifyEmail = configuration.GetOptionalString(ConfigKeys.ContactNotificationEmail);
                var subject = $"[Contact] {request.Subject}: {request.Name.Trim()}";
                var (htmlBody, textBody) = EmailTemplates.AdminNotification("New contact form submission",
                    [
                        ("Name", request.Name.Trim()),
                        ("Email", request.Email.Trim()),
                        ("Company", request.Company?.Trim()),
                        ("Subject", request.Subject),
                    ],
                    request.Message.Trim());

                await emailService.TrySendNotificationAsync(notifyEmail, subject, htmlBody, textBody, logger, "contact notification email");

                return Results.Ok(new { message = "Message received. We'll be in touch shortly." });
            }, logger, "submit contact form", new { subject = request.Subject });
        })
        .WithName("SubmitContactForm")
        .WithSummary("Submit the marketing contact form")
        .RequireRateLimiting(FoundationRateLimitPolicies.ContactForm)
        .RequireChallengeVerification();
    }
}
