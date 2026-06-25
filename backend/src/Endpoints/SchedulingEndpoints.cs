using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;


namespace Api.Endpoints;

public static class SchedulingEndpoints
{
    public static void MapSchedulingEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/sites/{siteId:guid}/scheduling")
            .WithTags("Scheduling")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        settings.MapGet("/", async (Guid siteId, ISchedulingService schedulingService, CancellationToken ct) =>
        {
            var result = await schedulingService.GetSettingsAsync(siteId, ct) ?? SchedulingSettingsInfo.Default(siteId);
            return Results.Ok(result);
        })
        .WithName("GetSchedulingSettings")
        .WithDescription("Get scheduling settings for a site")
        .Produces<SchedulingSettingsInfo>(StatusCodes.Status200OK);

        settings.MapPut("/", async (Guid siteId, UpsertSchedulingSettingsRequest request, ISchedulingService schedulingService, CancellationToken ct, ILogger<EndpointLoggerCategory> logger, IValidator<UpsertSchedulingSettingsRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await schedulingService.UpsertSettingsAsync(siteId, request, ct);
                await schedulingService.RecalculateScheduledRequestsAsync(siteId, ct);
                return Results.Ok(result);
            }, logger, "upsert scheduling settings", new { siteId });
        })
        .WithName("UpsertSchedulingSettings")
        .WithDescription("Create or update scheduling settings for a site")
        .Accepts<UpsertSchedulingSettingsRequest>("application/json")
        .Produces<SchedulingSettingsInfo>(StatusCodes.Status200OK);

        settings.MapDelete("/", async (Guid siteId, ISchedulingService schedulingService, CancellationToken ct) =>
        {
            var deleted = await schedulingService.DeleteSettingsAsync(siteId, ct);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("SchedulingSettings");
        })
        .WithName("DeleteSchedulingSettings")
        .WithDescription("Reset scheduling settings for a site to defaults");

    }
}
