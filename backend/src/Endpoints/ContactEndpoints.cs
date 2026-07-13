using Api.Configuration;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Npgsql;
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
            IDbConnectionFactory connectionFactory,
            IEmailService emailService,
            IConfiguration configuration,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                await using var conn = connectionFactory.CreateControlPlaneConnection();
                await conn.OpenAsync(ct);

                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO contact_submissions (id, name, email, company, subject, message, created_at_utc)
                    VALUES (@id, @name, @email, @company, @subject, @message, @createdAt)", conn);
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("name", request.Name.Trim());
                cmd.Parameters.AddWithValue("email", request.Email.Trim());
                cmd.Parameters.AddWithValue("company", (object?)request.Company?.Trim() ?? DBNull.Value);
                cmd.Parameters.AddWithValue("subject", request.Subject);
                cmd.Parameters.AddWithValue("message", request.Message.Trim());
                cmd.Parameters.AddWithValue("createdAt", DateTime.UtcNow);
                await cmd.ExecuteNonQueryAsync(ct);

                logger.LogInformation("Contact form submitted: {Email}, subject={Subject}", request.Email, request.Subject);

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
            }, logger, "submit contact form", new { email = request.Email, subject = request.Subject });
        })
        .WithName("SubmitContactForm")
        .WithSummary("Submit the marketing contact form")
        .RequireRateLimiting(FoundationRateLimitPolicies.ContactForm);
    }
}
