using Api.Configuration;
using Api.Helpers;
using Api.Middleware;
using Api.Repositories;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orkyo.Shared;

namespace Api.Endpoints;

public static class UserAnnouncementEndpoints
{
    public static void MapUserAnnouncementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/announcements")
            .RequireAuthorization()
            .WithTags("Announcements (User)")
            .WithMetadata(new SkipTenantResolutionAttribute());

        group.MapGet("/", GetActive)
            .WithName("GetActiveAnnouncements")
            .WithSummary("List active announcements for the current user");

        group.MapGet("/unread-count", GetUnreadCount)
            .WithName("GetUnreadAnnouncementCount")
            .WithSummary("Get unread announcement count");

        group.MapPost("/{id:guid}/read", MarkRead)
            .WithName("MarkAnnouncementRead")
            .WithSummary("Mark an announcement as read");

        // Public: the link in the announcement-email footer (the recipient may be logged out).
        // GET only renders a confirmation form, so a mail scanner or link prefetcher that follows
        // the link changes nothing; the form's POST does the opt-out.
        app.MapGet("/api/announcements/unsubscribe", [AllowAnonymous] (
            string? token, HttpContext httpContext, IConfiguration configuration) =>
        {
            RelaxCsp(httpContext);
            return Guid.TryParse(token, out var unsubscribeToken)
                ? UnsubscribePage.Confirm(unsubscribeToken)
                : InvalidLink(configuration);
        })
        .WithName("ConfirmUnsubscribeAnnouncements")
        .WithSummary("Confirmation page for unsubscribing from announcement emails")
        .WithTags("Announcements (User)")
        .WithMetadata(new SkipTenantResolutionAttribute());

        app.MapPost("/api/announcements/unsubscribe", [AllowAnonymous] async (
            HttpContext httpContext,
            IPlatformUserRepository userRepository,
            IConfiguration configuration,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            RelaxCsp(httpContext);
            var form = httpContext.Request.HasFormContentType
                ? await httpContext.Request.ReadFormAsync(ct)
                : null;
            if (!Guid.TryParse(form?["token"].ToString(), out var unsubscribeToken))
                return InvalidLink(configuration);

            var appBaseUrl = AppBaseUrl(configuration);
            try
            {
                if (!await userRepository.SetAnnouncementOptOutByTokenAsync(unsubscribeToken, ct))
                {
                    logger.LogWarning("announcement unsubscribe: token not found");
                    return InvalidLink(configuration);
                }

                logger.LogInformation("User opted out of announcement emails via unsubscribe link");
                return UnsubscribePage.Result(appBaseUrl, success: true,
                    "You're unsubscribed",
                    "You won't receive announcement emails anymore. You'll still see announcements in the app.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing announcement unsubscribe token");
                return UnsubscribePage.Result(appBaseUrl, success: false,
                    "Something went wrong", "We couldn't process your request. Please try again later.");
            }
        })
        .WithName("UnsubscribeAnnouncements")
        .WithSummary("Unsubscribe from announcement emails")
        .WithTags("Announcements (User)")
        .WithMetadata(new SkipTenantResolutionAttribute(), new CsrfExemptAttribute());
    }

    // The page renders inline styles and posts its own form; relax the API's strict
    // `default-src 'none'` CSP for exactly that.
    private static void RelaxCsp(HttpContext httpContext) =>
        httpContext.Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'";

    private static string AppBaseUrl(IConfiguration configuration) =>
        configuration.GetRequired(ConfigKeys.AppBaseUrl).TrimEnd('/');

    private static IResult InvalidLink(IConfiguration configuration) =>
        UnsubscribePage.Result(AppBaseUrl(configuration), success: false,
            "Invalid link", "This unsubscribe link is invalid or has expired.");

    private static async Task<IResult> GetActive(IAnnouncementService service, CurrentPrincipal principal, CancellationToken ct = default)
    {
        var userId = principal.RequireUserId();
        var announcements = await service.GetActiveForUserAsync(userId, ct);
        return Results.Ok(new { announcements });
    }

    private static async Task<IResult> GetUnreadCount(IAnnouncementService service, CurrentPrincipal principal, CancellationToken ct = default)
    {
        var userId = principal.RequireUserId();
        var count = await service.GetUnreadCountAsync(userId, ct);
        return Results.Ok(new { unreadCount = count });
    }

    private static async Task<IResult> MarkRead(Guid id, IAnnouncementService service, CurrentPrincipal principal, CancellationToken ct = default)
    {
        var userId = principal.RequireUserId();
        await service.MarkReadAsync(id, userId, ct);
        return Results.NoContent();
    }
}
