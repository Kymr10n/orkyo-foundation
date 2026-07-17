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

        // GET /api/announcements/unsubscribe?token=<uuid>
        // Public — clicked from the announcement-email footer (recipient may be logged out), so it
        // serves a small self-contained confirmation page rather than redirecting into the SPA.
        app.MapGet("/api/announcements/unsubscribe", [AllowAnonymous] async (
            string? token,
            HttpContext httpContext,
            IPlatformUserRepository userRepository,
            IConfiguration configuration,
            CancellationToken ct,
            ILogger<EndpointLoggerCategory> logger) =>
        {
            var appBaseUrl = configuration.GetRequired(ConfigKeys.AppBaseUrl).TrimEnd('/');

            // This page renders inline styles; relax the API's strict `default-src 'none'` CSP for it.
            httpContext.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";

            if (!Guid.TryParse(token, out var unsubscribeToken))
                return UnsubscribePage(appBaseUrl, success: false,
                    "Invalid link", "This unsubscribe link is invalid or has expired.");

            try
            {
                var found = await userRepository.SetAnnouncementOptOutByTokenAsync(unsubscribeToken, ct);

                if (!found)
                {
                    logger.LogWarning("announcement unsubscribe: token not found");
                    return UnsubscribePage(appBaseUrl, success: false,
                        "Invalid link", "This unsubscribe link is invalid or has expired.");
                }

                logger.LogInformation("User opted out of announcement emails via unsubscribe link");
                return UnsubscribePage(appBaseUrl, success: true,
                    "You're unsubscribed",
                    "You won't receive announcement emails anymore. You'll still see announcements in the app.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing announcement unsubscribe token");
                return UnsubscribePage(appBaseUrl, success: false,
                    "Something went wrong", "We couldn't process your request. Please try again later.");
            }
        })
        .WithName("UnsubscribeAnnouncements")
        .WithSummary("Unsubscribe from announcement emails")
        .WithTags("Announcements (User)")
        .WithMetadata(new SkipTenantResolutionAttribute());
    }

    /// <summary>
    /// Minimal self-contained, Orkyo-branded confirmation page for the public unsubscribe link.
    /// Uses the platform brand gradient (matches the announcement email's default branding).
    /// </summary>
    private static IResult UnsubscribePage(string appBaseUrl, bool success, string heading, string message)
    {
        var icon = success ? "&#10003;" : "&#33;";        // ✓ / !
        var iconBg = success
            ? "linear-gradient(135deg, #667eea 0%, #764ba2 100%)"
            : "#dc2626";
        var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>{heading} — Orkyo</title>
  <style>
    :root {{ color-scheme: light; }}
    * {{ box-sizing: border-box; }}
    body {{
      font-family: system-ui, -apple-system, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
      margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); padding: 24px; color: #18181b;
    }}
    .card {{
      background: #fff; width: 100%; max-width: 460px; border-radius: 16px; overflow: hidden;
      box-shadow: 0 20px 50px rgba(0,0,0,0.25); text-align: center;
    }}
    .brand {{
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
      color: #fff; padding: 22px; font-size: 22px; font-weight: 800; letter-spacing: .5px;
    }}
    .body {{ padding: 36px 40px 40px; }}
    .icon {{
      width: 64px; height: 64px; line-height: 64px; margin: 0 auto 22px; border-radius: 50%;
      background: {iconBg}; color: #fff; font-size: 30px; font-weight: 700;
    }}
    h1 {{ margin: 0 0 12px; font-size: 22px; }}
    p {{ margin: 0 0 28px; font-size: 15px; line-height: 1.6; color: #52525b; }}
    .btn {{
      display: inline-block; padding: 12px 26px; border-radius: 9px; text-decoration: none;
      font-size: 14px; font-weight: 600; color: #fff;
      background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
    }}
  </style>
</head>
<body>
  <div class=""card"">
    <div class=""brand"">Orkyo</div>
    <div class=""body"">
      <div class=""icon"">{icon}</div>
      <h1>{heading}</h1>
      <p>{message}</p>
      <a class=""btn"" href=""{appBaseUrl}"">Back to Orkyo</a>
    </div>
  </div>
</body>
</html>";
        return Results.Content(html, "text/html; charset=utf-8");
    }

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
