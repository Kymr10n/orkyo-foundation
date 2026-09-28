using Api.Configuration;
using Api.Endpoints.Admin;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class AnnouncementEndpoints
{
    public static void MapAnnouncementEndpoints(this WebApplication app)
    {
        var group = app.MapSiteAdminGroup("/announcements", "Announcements");

        group.MapGet("/", GetAll)
            .WithName("GetAnnouncements")
            .WithSummary("List all announcements");

        group.MapGet("/{id:guid}", GetById)
            .WithName("GetAnnouncement")
            .WithSummary("Get announcement by ID");

        group.MapPost("/", Create)
            .WithName("CreateAnnouncement")
            .WithSummary("Create a new announcement");

        group.MapPut("/{id:guid}", Update)
            .WithName("UpdateAnnouncement")
            .WithSummary("Update an existing announcement");

        group.MapDelete("/{id:guid}", Delete)
            .WithName("DeleteAnnouncement")
            .WithSummary("Delete an announcement");
    }

    private static async Task<IResult> GetAll(IAnnouncementService service, bool includeExpired = true, CancellationToken ct = default)
    {
        var announcements = await service.GetAllAsync(includeExpired, ct);
        return Results.Ok(new { announcements });
    }

    private static async Task<IResult> GetById(Guid id, IAnnouncementService service, CancellationToken ct = default)
    {
        var dto = await service.GetByIdAsync(id, ct);
        return EndpointHelpers.OkOrNotFound(dto, "Announcement", id);
    }

    private static async Task<IResult> Create(
        CreateAnnouncementRequest request,
        IValidator<CreateAnnouncementRequest> validator,
        IAnnouncementService service,
        CurrentPrincipal principal,
        CancellationToken ct = default)
        => await EndpointHelpers.ExecuteAsync(request, validator, async () =>
        {
            var announcement = await service.CreateAsync(request, principal.UserId, ct);
            return Results.Created($"/api/admin/announcements/{announcement.Id}", announcement);
        });

    private static async Task<IResult> Update(
        Guid id,
        UpdateAnnouncementRequest request,
        IValidator<UpdateAnnouncementRequest> validator,
        IAnnouncementService service,
        CurrentPrincipal principal,
        CancellationToken ct = default)
        => await EndpointHelpers.ExecuteAsync(request, validator, async () =>
        {
            var result = await service.UpdateAsync(id, request, principal.UserId, ct);
            return EndpointHelpers.OkOrNotFound(result, "Announcement");
        });

    private static async Task<IResult> Delete(Guid id, IAnnouncementService service, CancellationToken ct = default)
    {
        var deleted = await service.DeleteAsync(id, ct);
        return EndpointHelpers.NoContentOrNotFound(deleted, "Announcement", id);
    }
}
