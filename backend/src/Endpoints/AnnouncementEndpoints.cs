using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class AnnouncementEndpoints
{
    public static void MapAnnouncementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/announcements")
            .RequireAuthorization()
            .RequireRateLimiting("admin-operations")
            .WithTags("Announcements")
            .WithMetadata(new SkipTenantResolutionAttribute());

        group.MapGet("/", GetAll)
            .RequireSiteAdmin()
            .WithName("GetAnnouncements")
            .WithSummary("List all announcements");

        group.MapGet("/{id:guid}", GetById)
            .RequireSiteAdmin()
            .WithName("GetAnnouncement")
            .WithSummary("Get announcement by ID");

        group.MapPost("/", Create)
            .RequireSiteAdmin()
            .WithName("CreateAnnouncement")
            .WithSummary("Create a new announcement");

        group.MapPut("/{id:guid}", Update)
            .RequireSiteAdmin()
            .WithName("UpdateAnnouncement")
            .WithSummary("Update an existing announcement");

        group.MapDelete("/{id:guid}", Delete)
            .RequireSiteAdmin()
            .WithName("DeleteAnnouncement")
            .WithSummary("Delete an announcement");
    }

    private static async Task<IResult> GetAll(IAnnouncementService service, bool includeExpired = true)
    {
        var announcements = await service.GetAllAsync(includeExpired);
        return Results.Ok(new { announcements });
    }

    private static async Task<IResult> GetById(Guid id, IAnnouncementService service)
    {
        var dto = await service.GetByIdAsync(id);
        return dto is null ? ErrorResponses.NotFound("Announcement", id) : Results.Ok(dto);
    }

    private static async Task<IResult> Create(
        CreateAnnouncementRequest request,
        IAnnouncementService service,
        CurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var announcement = await service.CreateAsync(request, principal.UserId);
                return Results.Created($"/api/admin/announcements/{announcement.Id}", announcement);
            }, logger, "CreateAnnouncement");

    private static async Task<IResult> Update(
        Guid id,
        UpdateAnnouncementRequest request,
        IAnnouncementService service,
        CurrentPrincipal principal,
        ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync<AnnouncementDto>(async () =>
                await service.UpdateAsync(id, request, principal.UserId),
            logger, "UpdateAnnouncement");

    private static async Task<IResult> Delete(Guid id, IAnnouncementService service)
    {
        var deleted = await service.DeleteAsync(id);
        return deleted ? Results.NoContent() : ErrorResponses.NotFound("Announcement", id);
    }
}
