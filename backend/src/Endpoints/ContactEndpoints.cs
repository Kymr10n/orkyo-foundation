using System.Globalization;
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

                // Confirmation to the visitor, only when a person reads the inbox it points to:
                // it promises a reply, and replies to it land in that inbox. One per address per
                // day, so repeated submissions cannot turn the form into a mailer.
                if (!string.IsNullOrEmpty(notifyEmail))
                {
                    var visitor = request.Email.Trim();
                    var (confirmSubject, confirmHtml, confirmText) = EmailTemplates.GetContactConfirmationEmail();
                    try
                    {
                        await emailService.SendEmailAsync(visitor, "", confirmSubject, confirmHtml, confirmText, ct,
                            dedupeKey: $"contact-confirmation:{visitor.ToLowerInvariant()}:{time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
                            replyTo: notifyEmail);
                    }
                    catch (Exception ex) { logger.LogWarning(ex, "Failed to send contact confirmation email"); }
                }

                return Results.Ok(new { message = "Message received. We'll be in touch shortly." });
            }, logger, "submit contact form", new { subject = request.Subject });
        })
        .WithName("SubmitContactForm")
        .WithSummary("Submit the marketing contact form")
        .RequireRateLimiting(FoundationRateLimitPolicies.ContactForm)
        .RequireChallengeVerification();
    }
}
