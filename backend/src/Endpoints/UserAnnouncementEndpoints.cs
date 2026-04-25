using Api.Middleware;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
    }

    private static async Task<IResult> GetActive(IAnnouncementService service, CurrentPrincipal principal)
    {
        var userId = principal.RequireUserId();
        var announcements = await service.GetActiveForUserAsync(userId);
        return Results.Ok(new { announcements });
    }

    private static async Task<IResult> GetUnreadCount(IAnnouncementService service, CurrentPrincipal principal)
    {
        var userId = principal.RequireUserId();
        var count = await service.GetUnreadCountAsync(userId);
        return Results.Ok(new { unreadCount = count });
    }

    private static async Task<IResult> MarkRead(Guid id, IAnnouncementService service, CurrentPrincipal principal)
    {
        var userId = principal.RequireUserId();
        await service.MarkReadAsync(id, userId);
        return Results.NoContent();
    }
}
