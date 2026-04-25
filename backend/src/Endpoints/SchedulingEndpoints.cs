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
            .RequireTenantMembership();

        settings.MapGet("/", async (Guid siteId, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await schedulingService.GetSettingsAsync(siteId) ?? SchedulingSettingsInfo.Default(siteId);
                return Results.Ok(result);
            }, logger, "get scheduling settings", new { siteId });
        })
        .WithName("GetSchedulingSettings")
        .WithDescription("Get scheduling settings for a site")
        .Produces<SchedulingSettingsInfo>(StatusCodes.Status200OK);

        settings.MapPut("/", async (Guid siteId, UpsertSchedulingSettingsRequest request, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<UpsertSchedulingSettingsRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await schedulingService.UpsertSettingsAsync(siteId, request);
                await schedulingService.RecalculateScheduledRequestsAsync(siteId);
                return Results.Ok(result);
            }, logger, "upsert scheduling settings", new { siteId });
        })
        .WithName("UpsertSchedulingSettings")
        .WithDescription("Create or update scheduling settings for a site")
        .Accepts<UpsertSchedulingSettingsRequest>("application/json")
        .Produces<SchedulingSettingsInfo>(StatusCodes.Status200OK);

        settings.MapDelete("/", async (Guid siteId, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await schedulingService.DeleteSettingsAsync(siteId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("SchedulingSettings");
            }, logger, "delete scheduling settings", new { siteId });
        })
        .WithName("DeleteSchedulingSettings")
        .WithDescription("Reset scheduling settings for a site to defaults");

        var offTimes = app.MapGroup("/api/sites/{siteId:guid}/off-times")
            .WithTags("Scheduling")
            .RequireAuthorization()
            .RequireTenantMembership();

        offTimes.MapGet("/", async (Guid siteId, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await schedulingService.GetOffTimesAsync(siteId)),
            logger, "list off-times", new { siteId });
        })
        .WithName("GetOffTimes")
        .WithDescription("List all off-times for a site");

        offTimes.MapGet("/{offTimeId:guid}", async (Guid siteId, Guid offTimeId, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await schedulingService.GetOffTimeByIdAsync(siteId, offTimeId);
                return result == null ? ErrorResponses.NotFound("OffTime", offTimeId) : Results.Ok(result);
            }, logger, "get off-time", new { siteId, offTimeId });
        })
        .WithName("GetOffTimeById")
        .WithDescription("Get a specific off-time by ID");

        offTimes.MapPost("/", async (Guid siteId, CreateOffTimeRequest request, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<CreateOffTimeRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await schedulingService.CreateOffTimeAsync(siteId, request);
                return Results.Created($"/api/sites/{siteId}/off-times/{result.Id}", result);
            }, logger, "create off-time", new { siteId });
        })
        .WithName("CreateOffTime")
        .WithDescription("Create a new off-time entry for a site");

        offTimes.MapPut("/{offTimeId:guid}", async (Guid siteId, Guid offTimeId, UpdateOffTimeRequest request, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateOffTimeRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await schedulingService.UpdateOffTimeAsync(siteId, offTimeId, request);
                return result == null ? ErrorResponses.NotFound("OffTime", offTimeId) : Results.Ok(result);
            }, logger, "update off-time", new { siteId, offTimeId });
        })
        .WithName("UpdateOffTime")
        .WithDescription("Update an existing off-time entry");

        offTimes.MapDelete("/{offTimeId:guid}", async (Guid siteId, Guid offTimeId, ISchedulingService schedulingService, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await schedulingService.DeleteOffTimeAsync(siteId, offTimeId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("OffTime", offTimeId);
            }, logger, "delete off-time", new { siteId, offTimeId });
        })
        .WithName("DeleteOffTime")
        .WithDescription("Delete an off-time entry");
    }
}
